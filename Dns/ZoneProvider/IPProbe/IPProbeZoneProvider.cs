using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using static System.Threading.Tasks.Task;

namespace Dns.ZoneProvider.IPProbe
{
    /// <summary>
    /// IPProbeZoneProvider map via configuration a set of monitored IPs to host A records.
    /// Various monitoring strategies are implemented to detect IP health.
    /// Health IP addresses are added to the Zone.
    /// </summary>
    public class IPProbeZoneProvider : BaseZoneProvider
    {
        private IPProbeProviderOptions options;

        private State state { get; set; }
        private CancellationToken ct { get; set; }
        private Task runningTask { get; set; }

        /// <summary>Initialize ZoneProvider</summary>
        /// <param name="serviceCollection"></param>
        /// <param name="config">ZoneProvider Configuration Section</param>
        /// <param name="zoneName">Zone suffix</param>
        public override void Initialize(IServiceProvider serviceCollection, IConfiguration config, string zoneName)
        {
            options = config.Get<IPProbeProviderOptions>();
            if (options == null)
            {
                throw new Exception("Error loading IPProbeProviderOptions");
            }

            // load up initial state from options
            state = new State(options);
            Zone = zoneName;
        }

        public void ProbeLoop(CancellationToken ct)
        {
            Console.WriteLine("Probe loop started");

            ParallelOptions options = new() { CancellationToken = ct, MaxDegreeOfParallelism = 4 };

            while (!ct.IsCancellationRequested)
            {
                DateTime batchStartTime = DateTime.UtcNow;

                Parallel.ForEach(state.Targets, options, probe =>
                {
                    DateTime startTime = DateTime.UtcNow;
                    bool result = probe.ProbeFunction(probe.Address, probe.TimeoutMilliseconds);
                    TimeSpan duration = DateTime.UtcNow - startTime;
                    probe.AddResult(new ProbeResult { StartTime = startTime, Duration = duration, Available = result });
                });

                Run(() => GetZone(state), ct).ContinueWith(t => Notify(t.Result), ct);

                TimeSpan batchDuration = DateTime.UtcNow - batchStartTime;
                Console.WriteLine("Probe batch duration {0}", batchDuration);

                // wait remainder of Polling Interval
                int remainingWaitTimeout = (this.options.PollingIntervalSeconds * 1000) -(int)batchDuration.TotalMilliseconds;
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

        internal static IEnumerable<ZoneRecord>GetZoneRecords(State state)
        {
            foreach(Host host in state.Hosts)
            {
                IEnumerable<IPAddress> availableAddresses = host.AddressProbes
                    .Where(addr => addr.IsAvailable)
                    .Select(addr => addr.Address);

                if(host.AvailabilityMode == AvailabilityMode.First) availableAddresses = availableAddresses.Take(1);

                // materialize query
                IPAddress[] addresses = availableAddresses.ToArray();

                if (addresses.Length == 0)
                {
                    // no hosts with empty recordsets
                    continue;
                }


                yield return new ZoneRecord
                {
                    Host = host.Name + Zone,
                    Addresses = addresses,
                    Count = addresses.Length,
                    Type = ResourceType.A,
                    Class = ResourceClass.IN,
                };
            }
        }

        private Zone GetZone(State state)
        {
            IEnumerable<ZoneRecord> zoneRecords = GetZoneRecords(state);

            Zone zone = new() { Suffix = Zone, Serial = _serial };
            zone.Initialize(zoneRecords);

            // increment serial number
            _serial++;
            return zone;
        }
    }
}