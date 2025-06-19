using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;
using Dns.Models.Traefik;
using Dns.Services;
using Microsoft.Extensions.Logging;
using static System.Threading.Tasks.Task;

namespace Dns.ZoneProvider.Traefik;

/// <summary>
/// IPProbeZoneProvider map via configuration a set of monitored IPs to host A records.
/// Various monitoring strategies are implemented to detect IP health.
/// Health IP addresses are added to the Zone.
/// </summary>
public partial class TraefikZoneProvider(ILogger<TraefikZoneProvider> logger, TraefikClientService traefikClientService, IDnsResolver dnsResolver)
    : BaseZoneProvider(dnsResolver)
{
    private CancellationToken ct          { get; set; }
    private Task              RunningTask { get; set; }

    /// <summary>Initialize ZoneProvider</summary>
    /// <param name="zoneOptions">ZoneProvider Configuration Section</param>
    public override void Initialize(ZoneOptions zoneOptions)
    {
        Zone = zoneOptions.Name;
        traefikClientService.Initialize(zoneOptions);
        
        base.Initialize(zoneOptions);
    }

    private void ProbeLoop(CancellationToken ct)
    {
        logger.LogInformation("Probe loop started");

        ParallelOptions options = new() { CancellationToken = ct, MaxDegreeOfParallelism = 4 };

        while (!ct.IsCancellationRequested)
        {
            var batchStartTime = DateTime.UtcNow;

            Run(GetZone, ct).ContinueWith(t => Notify(t.Result), ct);

            var batchDuration = DateTime.UtcNow - batchStartTime;
            logger.LogInformation("Probe batch duration {BatchDuration}", batchDuration);

            ct.WaitHandle.WaitOne(10 * 1000);
        }
    }

    public override void Dispose()
    {
        // cleanup
    }

    public override void Start(CancellationToken ct)
    {
        ct.Register(Stop);
        RunningTask = Run(()=>ProbeLoop(ct), ct);
    }

    private void Stop() => RunningTask.Wait(ct);

    private async Task<IEnumerable<ZoneRecord>> GetZoneRecords(/*State state*/)
    {

        return  (from host in await traefikClientService.GetRoutes()
            where (host.Provider.Equals("docker", StringComparison.InvariantCultureIgnoreCase) || host.EntryPoints.Contains("web")) && host.Tls == null && host.Rule.Contains(Zone)
            select new ZoneRecord
            {
                Host      = CreateHostName(host),
                Addresses = [traefikClientService.GetDockerHostInternalIp().ToString()],
                Count     = 1,
                Type      = ResourceType.A,
                Class     = ResourceClass.IN,
            }).ToList();
    }

    private static string CreateHostName(Route host)
    {
        var regex = MyRegex();

        var matches = regex.Matches(host.Rule);

        return matches.Select(g => g.Groups[2]).FirstOrDefault(x => x.Value.Contains(Zone))?.Value;
    }

    private Zone GetZone(/*State state*/)
    {
        var zoneRecords = GetZoneRecords(/*state*/).Result;

        Zone zone = new() { Suffix = Zone, Serial = Serial };
        zone.Initialize(zoneRecords);

        // increment serial number
        Serial++;
        return zone;
    }

    [GeneratedRegex(@"([a-zA-Z0-9]+)\(\`([a-zA-Z0-9.\-_\']*)\`\)(\ |\|\||\t|\r|\s)*", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MyRegex();
}