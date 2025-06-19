using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;
using static System.Threading.Tasks.Task;

namespace Dns.ZoneProvider.IPProbe;

/// <summary>
/// IPProbeZoneProvider map via configuration a set of monitored IPs to host A records.
/// Various monitoring strategies are implemented to detect IP health.
/// Health IP addresses are added to the Zone.
/// </summary>
public class IPProbeZoneProvider(IDnsResolver resolver) : BaseZoneProvider(resolver)
{
    private IPProbeProviderOptions options;

    private State             state       { get; set; }
    private CancellationToken ct          { get; set; }
    private Task              runningTask { get; set; }

    /// <summary>Initialize ZoneProvider</summary>
    /// <param name="serviceCollection"></param>
    /// <param name="config">ZoneProvider Configuration Section</param>
    /// <param name="zoneName">Zone suffix</param>
    public override void Initialize(ZoneOptions zoneOptions)
    {
        options = new();//TODO: FIXME zoneOptions.ProviderSettings;
        if (options == null)
        {
            throw new("Error loading IPProbeProviderOptions");
        }

        // load up initial state from options
        state = new(options);
        Zone  = zoneOptions.Name;
        
        base.Initialize(zoneOptions);
    }

    public void ProbeLoop(CancellationToken ct)
    {
        Console.WriteLine("Probe loop started");

        ParallelOptions options = new() { CancellationToken = ct, MaxDegreeOfParallelism = 4 };

        while (!ct.IsCancellationRequested)
        {
            var batchStartTime = DateTime.UtcNow;

            Parallel.ForEach(state.Targets, options, probe =>
            {
                var startTime = DateTime.UtcNow;
                var result = probe.ProbeFunction(probe.Address, probe.TimeoutMilliseconds);
                var duration = DateTime.UtcNow - startTime;
                probe.AddResult(new() { StartTime = startTime, Duration = duration, Available = result });
            });

            Run(() => GetZone(state), ct).ContinueWith(t => Notify(t.Result), ct);

            var batchDuration = DateTime.UtcNow - batchStartTime;
            Console.WriteLine("Probe batch duration {0}", batchDuration);

            // wait remainder of Polling Interval
            var remainingWaitTimeout = (this.options.PollingIntervalSeconds * 1000) -(int)batchDuration.TotalMilliseconds;
            if(remainingWaitTimeout > 0) ct.WaitHandle.WaitOne(remainingWaitTimeout);
        }
    }

    public override void Dispose()
    {
        // cleanup
    }

    public override void Start(CancellationToken ct)
    {
        ct.Register(Stop);
        runningTask = Run(()=>ProbeLoop(ct), ct);
    }

    private void Stop() => runningTask.Wait(ct);

    internal static IEnumerable<ZoneRecord> GetZoneRecords(State state)
    {
        foreach(var host in state.Hosts)
        {
            var availableAddresses = host.AddressProbes
                                         .Where(addr => addr.IsAvailable)
                                         .Select(addr => addr.Address);

            if(host.AvailabilityMode == AvailabilityMode.First) availableAddresses = availableAddresses.Take(1);

            // materialize query
            var addresses = availableAddresses.Select(s => s.ToString()).ToList();

            if (addresses.Count == 0)
            {
                // no hosts with empty recordsets
                continue;
            }


            yield return new()
            {
                Host = host.Name + Zone,
                Addresses = addresses,
                Count = addresses.Count,
                Type = ResourceType.A,
                Class = ResourceClass.IN,
            };
        }
    }

    private Zone GetZone(State state)
    {
        var zoneRecords = GetZoneRecords(state);

        Zone zone = new() { Suffix = Zone, Serial = Serial };
        zone.Initialize(zoneRecords);

        // increment serial number
        Serial++;
        return zone;
    }
}