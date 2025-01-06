using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dns.Models.Traefik;
using Dns.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static System.Threading.Tasks.Task;

namespace Dns.ZoneProvider.Traefik;

/// <summary>
/// IPProbeZoneProvider map via configuration a set of monitored IPs to host A records.
/// Various monitoring strategies are implemented to detect IP health.
/// Health IP addresses are added to the Zone.
/// </summary>
public partial class TraefikZoneProvider : BaseZoneProvider
{
    private static IServiceProvider _services;
    //private IPProbeProviderOptions options;

    //private State state { get; set; }
    private CancellationToken ct          { get; set; }
    private Task              RunningTask { get; set; }

    /// <summary>Initialize ZoneProvider</summary>
    /// <param name="serviceCollection"></param>
    /// <param name="services"></param>
    /// <param name="config">ZoneProvider Configuration Section</param>
    /// <param name="zoneName">Zone suffix</param>
    public override void Initialize(IServiceProvider services, IConfiguration config, string zoneName)
    {
/*
            this.options = config.Get<IPProbeProviderOptions>();
            if (options == null)
            {
                throw new Exception("Error loading IPProbeProviderOptions");
            }

            // load up initial state from options
            this.state = new State(options);
*/
        _services = services;
        Zone = zoneName;
    }

    private void ProbeLoop(CancellationToken ct)
    {
        _services.GetService<ILogger<TraefikZoneProvider>>()?.LogInformation("Probe loop started");

        ParallelOptions options = new() { CancellationToken = ct, MaxDegreeOfParallelism = 4 };

        while (!ct.IsCancellationRequested)
        {
            var batchStartTime = DateTime.UtcNow;

            Run(() => GetZone(), ct).ContinueWith(t => Notify(t.Result), ct);

            var batchDuration = DateTime.UtcNow - batchStartTime;
            _services.GetService<ILogger<TraefikZoneProvider>>()?.LogInformation($"Probe batch duration {batchDuration}");

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

    private static async Task<IEnumerable<ZoneRecord>> GetZoneRecords(/*State state*/)
    {
        var traefik = _services.GetService<TraefikClientService>();

        if (traefik == null) return null;
        return  (from host in await traefik.GetRoutes()
            where (host.Provider.Equals("docker", StringComparison.InvariantCultureIgnoreCase) || host.EntryPoints.Contains("web")) && host.Tls == null && host.Rule.Contains(Zone)
            select new ZoneRecord
            {
                Host = CreateHostName(host),
                Addresses = new[] { traefik.GetDockerHostInternalIp() },
                Count = 1,
                Type = ResourceType.A,
                Class = ResourceClass.IN,
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

        Zone zone = new() { Suffix = Zone, Serial = _serial };
        zone.Initialize(zoneRecords);

        // increment serial number
        _serial++;
        return zone;
    }

    [GeneratedRegex(@"([a-zA-Z0-9]+)\(\`([a-zA-Z0-9.\-_\']*)\`\)(\ |\|\||\t|\r|\s)*", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MyRegex();
}