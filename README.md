# csharp-dns-server

[![GitHub Actions Status](https://github.com/stephbu/csharp-dns-server/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/stephbu/csharp-dns-server/actions/workflows/ci.yml) [![codecov](https://codecov.io/github/MBeijer/csharp-dns-server/branch/master/graph/badge.svg?token=WBG52LZI5G)](https://codecov.io/github/MBeijer/csharp-dns-server)

Fully functional software-extensible DNS server written in C# targeting .NET 10. Ensure the .NET 10 SDK is installed before building or testing.

The project was conceived while working to reduce the cost of cloud datacentre "stamps", providing robust discovery services within a datacentre, while specifically removing the need for expensive load-balancer devices.  The DNS Service would support software-defined/pluggable discovery of healthy hosts and services, and round-robin DNS services.  Such that clients may re-resolve, and retry connectivity instead.

## Licence
This software is licenced under MIT terms that permits reuse within proprietary software provided all copies of the licensed software include a copy of the MIT License terms and the copyright notice.  See [LICENSE.md](./LICENSE.md)

## Getting Started

```
// clone the repo
>> cd $repo-root
>> git clone https://github.com/stephbu/csharp-dns-server

// check you can build the project
>> cd $repo-root/csharp-dns-server
>> dotnet build csharp-dns-server.sln

// check that the tests run
>> dotnet test csharp-dns-server.sln

// use DIG query appconfig'd local server
>> dig -p 5335 @127.0.0.1 www.google.com A 

```

> **Note:** The solution targets `net10.0`; all commands above assume the .NET 10 SDK is available on your PATH.

## Gotchas
- if you're running on Windows with Docker Tools installed, Docker uses the ICS SharedAccess service to provide DNS resolution for Docker containers - this listens on UDP:53, and will conflict with the DNS project.  Either turn off the the service (```net stop SharedAccess```), or change the UDP port.

## Continuous Integration
This fork uses Jenkins for CI/CD (`Jenkinsfile`), not GitHub Actions.

Pipeline highlights:
- builds Docker images from `Dockerfile`
- runs the test suite inside the build/test container (`dotnet test`)
- publishes test/coverage artifacts (including Codecov upload)
- pushes images to Docker Hub for `master` (production tag) and `dev` (`-dev` tag)

Docker image:
- `mbeijer/dns-traefik` (`latest` on `master`, `latest-dev` on `dev`)
- https://hub.docker.com/r/mbeijer/dns-traefik

## Features

As written, the server has the following features:

 - Pluggable Zone Resolver.  Host one or more zones locally, and run your code to resolve names in that zone.  Enables many complex scenarios such as:
 - round-robin load-balancing.  Distribute load and provide failover with a datacentre without expensive hardware.
 - health-checks.  While maintaining a list of machines in round-robin for a name, the code performs periodic healthchecks against the machines, if necessary removing machines that fail the health checks from rotation.
 - Delegates all other DNS lookup to host machines default DNS server(s)
 - Database-backed authoritative zones (CRUD via API + runtime resolution)
 - Automatic setup of zones for docker instances running on a specific docker host via Traefik route discovery
 - Authoritative secondary support primitives: `AXFR`/`IXFR` over TCP and `NOTIFY` request/ack handling

The DNS server has a built-in ASP.NET Core web host with:
- Swagger/OpenAPI UI
- JWT-protected zone management API
- resolver dump endpoints (legacy compatibility)
- optional React SPA hosting from `Dns.Cli/wwwroot`

## Zone Providers
The server ships with pluggable providers that publish authoritative data into `SmartZoneResolver`:

- **CSV/AP provider** (`Dns.ZoneProvider.AP.APZoneProvider`) - file-watcher based CSV import (`MachineFunction`, `StaticIP`, `MachineName`) that groups addresses into A records per function. See `docs/providers/AP_provider.md`.
- **IPProbe provider** (`Dns.ZoneProvider.IPProbe.IPProbeZoneProvider`) - active probing of configured targets with health-based A record publication. See `docs/providers/IPProbe_provider.md`.
- **BIND provider** (`Dns.ZoneProvider.Bind.BindZoneProvider`) - file-watcher based BIND zone parser/validator (`$ORIGIN`, `$TTL`, SOA/NS/A/AAAA/CNAME/PTR/MX/TXT`) that only publishes valid zones. See `docs/providers/BIND_provider.md`.
- **Traefik provider** (`Dns.ZoneProvider.Traefik.TraefikZoneProvider`) - polls Traefik routes and emits A records for matching host rules in the configured zone.
- **Database provider** (`Dns.ZoneProvider.DatabaseZoneProvider`) - continuously loads enabled zones/records from the application database and serves them authoritatively.

Provider settings are configured per zone under `server.zones[*].providerSettings` with a `$type` discriminator:
- `ipprobe` for `IPProbeZoneProvider`
- `traefik` for `TraefikZoneProvider`
- `filewatcher` for file-backed providers (`APZoneProvider`, `BindZoneProvider`)
- no provider settings required for `DatabaseZoneProvider`

### Provider Configuration Examples
```json
{
  "server": {
    "zones": [
      {
        "name": ".example.com",
        "provider": "Dns.ZoneProvider.Bind.BindZoneProvider",
        "providerSettings": {
          "$type": "filewatcher",
          "fileName": "/etc/dns/example.com.zone"
        }
      },
      {
        "name": ".internal",
        "provider": "Dns.ZoneProvider.Traefik.TraefikZoneProvider",
        "providerSettings": {
          "$type": "traefik",
          "traefikUrl": "https://traefik.local",
          "username": "traefik-user",
          "password": "traefik-password",
          "dockerHostInternalIp": "10.0.0.10"
        }
      },
      {
        "provider": "Dns.ZoneProvider.DatabaseZoneProvider"
      }
    ]
  }
}
```

### BIND Provider Configuration
Add the provider via `appsettings.json` using the same `server.zones[]` shape used by all providers:

```json
{
  "server": {
    "zones": [
      {
        "name": ".example.com",
        "provider": "Dns.ZoneProvider.Bind.BindZoneProvider",
        "providerSettings": {
          "$type": "filewatcher",
          "fileName": "C:/zones/example.com.zone"
        }
      }
    ]
  }
}
```

The provider reads the file whenever it changes (a 10-second settlement window avoids partial writes), validates the directives/records, and only publishes `A`/`AAAA` data to SmartZoneResolver when the parse succeeds.  All other record types are parsed/validated so that zone files failing to meet RFC expectations never poison the active zone.

### Zone Transfer / Notify Configuration
`AXFR` and `IXFR` are only served over TCP when zone transfer is enabled and the caller IP is allowlisted.

```json
{
  "server": {
    "dnsListener": {
      "port": 53,
      "tcpPort": 53
    },
    "zoneTransfer": {
      "enabled": true,
      "allowTransfersFrom": [
        "192.0.2.10",
        "198.51.100.0/24"
      ],
      "notifySecondaries": [
        "192.0.2.20:53"
      ],
      "notifyPollIntervalSeconds": 5,
      "injectedNsAddress": "192.0.2.53"
    }
  }
}
```

- `allowTransfersFrom`: required ACL for incoming AXFR/IXFR requests.
- `notifySecondaries`: optional list of `ip[:port]` targets that receive outbound DNS NOTIFY whenever a zone serial changes.
- `injectedNsAddress`: optional fallback address/target used only when the server auto-injects an apex NS for AXFR validity; IPv4 -> `A`, IPv6 -> `AAAA`, hostname -> `CNAME`.
- UDP AXFR/IXFR requests are refused by design; use TCP transport.

## React SPA (NSwag + Redux)
A React SPA is available under `Dns.Spa/` and is designed to interface with the API in `Dns.Cli`.

Highlights:
- API client generation via **NSwag** (`Dns.Spa/nswag.json`)
- Redux Toolkit state management for async API calls (`auth` and `zones` slices)
- UI components and theming with **MUI** (`@mui/material`)
- Vite dev proxy for `/dns`, `/user`, `/dump` to `http://localhost:5000`

### Run API + SPA locally
1. Start API:
```bash
cd Dns.Cli
dotnet run -- ./appsettings.json
```
2. Start SPA:
```bash
cd Dns.Spa
npm install
npm run generate:api
npm run dev
```

Optional:
- set `VITE_API_BASE_URL` if your API runs on a different host/port.
- regenerate the NSwag client whenever API contracts change.

### Host SPA from Dns.Cli
`Dns.Cli` is configured with ASP.NET Core SPA middleware:
- **Development**: `UseSpa(...UseProxyToSpaDevelopmentServer("http://localhost:5173"))`
- **Production**: serves static files from `wwwroot`
To host the React SPA via ASP.NET:

```bash
cd Dns.Spa
npm install
npm run generate:api
npm run build

cd ../Dns.Cli
dotnet run -- ./appsettings.json
```

On `dotnet build`/`dotnet publish`, `Dns.Cli` copies files from `Dns.Spa/dist` into `Dns.Cli/wwwroot` when `dist` exists.

### Docker build
`Dockerfile` uses a Node build stage to compile `Dns.Spa` and then copies `dist` into the .NET build stage so the final ASP.NET image serves the SPA in production mode.

### Get Started with Docker (Compose)
Example `docker-compose.yml` for running the published Docker Hub image with DNS + API + Traefik routing:

```yaml
services:
  dns-service:
    image: mbeijer/dns-traefik:latest
    environment:
      ASPNETCORE_URLS: "http://*:5000"
    restart: always
    volumes:
      - ./appsettings.json:/app/appsettings.json
      - ./data:/app/data
    ports:
      - "5000/tcp"
      - "53:5335/udp"
      - "53:5335/tcp"
    dns:
      - 8.8.8.8
    networks:
      - traefik_compose
    labels:
      - "traefik.http.routers.dns-service.rule=Host(`dns-service-docker-dns.local`) || Host(`dns-service-docker-dns.internal`) || Host(`dns.local`) || Host(`dns.internal`)"
      - "traefik.http.routers.dns-service.entrypoints=web"
      - "traefik.http.routers.dns-service-secured.rule=Host(`dns-service-docker-dns.local`) || Host(`dns-service-docker-dns.internal`) || Host(`dns.local`) || Host(`dns.internal`)"
      - "traefik.http.routers.dns-service-secured.entrypoints=websecure"
      - "traefik.http.routers.dns-service-secured.tls=true"
      - "traefik.http.routers.dns-service-secured.tls.certresolver=myresolver"
      - "traefik.http.services.dns-service.loadbalancer.server.port=5000"

networks:
  traefik_compose:
    external: true
    name: traefik_compose
```

Run it:
```bash
docker compose up -d
```

Notes:
- DNS requests to host port `53` are forwarded to container port `5335` (UDP/TCP).
- The API/Swagger host runs on container port `5000`.
- Keep `./appsettings.json` in sync with your desired providers/zones; it is mounted directly into `/app/appsettings.json`.
- Image source: https://hub.docker.com/r/mbeijer/dns-traefik

### Import BIND Zone Into Database Zone
You can convert a BIND zone file into a database-backed zone via the DNS API:

`POST /dns/zones/import-bind`

```json
{
  "fileName": "/path/to/example.com.zone",
  "zoneSuffix": "example.com",
  "enabled": true,
  "replaceExistingRecords": true
}
```

Behavior:
- Parses the file using the same BIND parser used by `BindZoneProvider`.
- Upserts the database zone by `suffix` (creates if missing, updates if present).
- Flattens each BIND RR datum into `zone_records` rows.

To import and switch all currently active BIND providers at runtime:

`POST /dns/zones/import-active-bind`

```json
{
  "replaceExistingRecords": true,
  "enableImportedZones": true
}
```

This operation:
- Imports every currently active `BindZoneProvider` zone file into the DB.
- Disables those BIND providers in the running process after successful import.

## Documentation
- [Product requirements](docs/product_requirements.md) describe the current roadmap, observability goals, and .NET maintenance plans.
- [Project priorities & plan](docs/priorities.md) outline the P0/P1/P2 focus areas plus execution notes (DI migration, OpenTelemetry instrumentation).
- [Task list](docs/task_list.md) captures the prioritized backlog that tracks to those priorities.
- [Protocol references](docs/references.md) list the RFCs and supporting standards that guide implementation.
- [AGENTS guide](AGENTS.md) explains how automation/AI contributors should work within this repository.

## Interesting Possible Uses
Time-based constraints such as parental controls to block a site, e.g. Facebook.
Logging of site usage e.g. company notifications

## Challenges

### Testing

Two phases of testing was completed.

1) Verification that the bit-packing classes correctly added and removed bits in correct Endian order, complicated by network bitpacking in reverse order to Windows big-endian packing.

2) Protocol verification - that well known messages were correctly decoded and re-encoded using the bit-packing system.

Much time was spent using Netmon to capture real DNS challenges and verify that the C# DNS server responded appropriately.

### Endianness Support
The DNS protocol uses **network byte order (big-endian)** for all multi-byte values. The codebase is designed to work correctly on both little-endian (x86, x64, ARM) and big-endian systems:

- The `SwapEndian()` extension methods in `Dns/Extensions.cs` conditionally swap bytes based on `BitConverter.IsLittleEndian`.
- Semantic aliases `NetworkToHost()` and `HostToNetwork()` provide clarity when converting DNS wire format.
- Unit tests in `dnstest/EndianTests.cs` validate correct byte order handling.

### DNS-Sec
No effort made to handle or respond to DNS-Sec challenges.

## Contribution Guide
Pull Requests, Bug Reports, and Feature Requests are most welcome.  

### Contribution Workflow
Suggested workflow for PRs is

1. Make a fork of csharp-dns-server/master in your own repository.
2. Create a branch in your own repo to entirely encapsulate all your proposed changes
3. Make your changes, add documentation if you need it, markdown text preferred.
4. Squash your commits into a single change [(Find out how to squash here)](http://stackoverflow.com/questions/616556/how-do-you-squash-commits-into-one-patch-with-git-format-patch)
5. Submit a PR, and put in comments anything that you think I'll need to help merge and evaluate the changes

If you are using automated tooling or AI agents, please review [AGENTS.md](AGENTS.md) to ensure you follow the approved scope and workflow.

### Licence Reminder
All contributions must be licenced under the same MIT terms, do include a header file to that effect.
