# Automatic secondary replication

## Overview

Automatic secondary replication lets one `csharp-dns-server` instance discover and mirror every zone currently served by another instance. It is intended for deployments where both the primary and secondary run this project.

Zone discovery uses a project-specific catalog stream on the normal DNS TCP listener. Zone contents use DNS AXFR. The primary does not need a second control or web port, and the secondary does not periodically poll an HTTP endpoint.

## Synchronization sequence

1. The secondary connects to the configured `secondarySync.master` DNS TCP endpoint.
2. It sends an AXFR subscription request for the reserved `_dns-zone-catalog` name.
3. The primary applies `zoneTransfer.enabled` and `zoneTransfer.allowTransfersFrom` to the connection.
4. An accepted connection receives a complete catalog containing the canonical zone name and current SOA serial of every active resolver zone.
5. The secondary requests TCP AXFR for every missing zone and every zone whose serial differs from its last-known copy.
6. The catalog TCP connection remains open. Resolver changes cause the primary to send a fresh complete catalog. A 30-second full-catalog heartbeat detects dead connections.
7. If the connection closes, the secondary reconnects after `reconnectDelaySeconds`. Every reconnect starts with a complete catalog, so changes missed while disconnected are reconciled automatically.

Each subscriber has an independent catalog stream. One primary can therefore serve multiple secondary instances concurrently.

## Primary configuration

```json
{
  "server": {
    "dnsListener": {
      "port": 5335,
      "tcpPort": 5335
    },
    "zoneTransfer": {
      "enabled": true,
      "allowTransfersFrom": [
        "secondary-1.example.net",
        "secondary-2.example.net",
        "2001:db8:20::/64"
      ]
    }
  }
}
```

ACL entries may be exact IPv4 or IPv6 addresses, CIDRs, DNS hostnames, or `*`. Hostname entries resolve through the primary host's system resolver; every A and AAAA result is authorized for five minutes. Prefer narrow entries over `*`.

The source address observed by the application must match the ACL. Verify this when a reverse proxy, Docker userland proxy, NAT gateway, or load balancer sits in front of TCP DNS.

## Secondary configuration

```json
{
  "server": {
    "zones": [
      {
        "provider": "Dns.ZoneProvider.DatabaseZoneProvider"
      }
    ],
    "secondarySync": {
      "enabled": true,
      "master": "primary.example.net:53",
      "reconnectDelaySeconds": 5,
      "cacheFile": "/app/data/secondary-zones.json"
    }
  }
}
```

`master` accepts a hostname, an IPv4 address, an IPv6 address, or an endpoint with a port. Brackets are required when specifying a port for IPv6, for example `[2001:db8::53]:5353`.

`cacheFile` is optional. For Docker, place it on a persistent volume. The provider atomically replaces the cache after a successful catalog change and loads it before connecting on the next start.

## Zone precedence and removal

Replicated zones are served from a dedicated high-priority resolver. If the secondary also has a local zone with the same suffix, the replicated copy wins while that zone remains in the primary catalog. The local copy is not overwritten or deleted. If the primary later removes the zone, the replicated overlay is removed and the local copy becomes authoritative again.

Other local zones continue to work normally. A secondary may also enable its own `zoneTransfer` settings and act as a primary for downstream instances.

## Admin visibility

The web administration zone overview includes zones from every active provider. Database-backed zones remain editable, while replicated, Traefik, BIND, probe, and other runtime-provider zones are identified by their source and displayed read-only. When a local database zone and a higher-priority provider zone use the same suffix, both rows remain visible so the configured zone is not hidden from administration.

## Network and security requirements

- Expose the DNS listener over TCP between primary and secondary. AXFR and the catalog stream do not use UDP.
- Keep idle TCP sessions alive through firewalls and NAT for longer than the 30-second heartbeat interval.
- Permit separate short-lived TCP connections for AXFR in addition to the persistent catalog connection.
- Catalog and AXFR data are authenticated only by source-address ACL and are not encrypted. Use a private routed network, WireGuard, IPsec, or another trusted tunnel for sensitive zones.
- Keep `cacheFile` on storage writable by the container user and protect it as zone data.

## Failure behavior

The secondary retains its last successfully transferred version when a transfer fails or the primary becomes unreachable. A changed zone is published only after a complete AXFR ending in the matching closing SOA. A valid catalog can remove a replicated zone; a broken or rejected catalog connection cannot.

An unreachable UDP NOTIFY target is logged as a warning and does not stop the primary DNS service or its active catalog streams.
