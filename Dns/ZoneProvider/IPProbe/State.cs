using System.Collections.Generic;
using System.Net;

namespace Dns.ZoneProvider.IPProbe
{
    internal class State
    {
        internal readonly HashSet<Target> Targets = new(new Target.Comparer());
        internal readonly HashSet<Host>   Hosts   = new();

        internal State(IPProbeProviderOptions options)
        {
            foreach (var host in options.Hosts)
            {
                Host hostResult = new() { Name = host.Name, AvailabilityMode = host.AvailabilityMode };

                foreach (var address in host.Ip)
                {
                    Target addressProbe = new()
                    {
                        Address = IPAddress.Parse(address),
                        ProbeFunction = Strategy.Get(host.Probe),
                        TimeoutMilliseconds = host.Timeout,
                    };

                    if (Targets.TryGetValue(addressProbe, out var preExisting))
                    {
                        hostResult.AddressProbes.Add(preExisting);
                    }
                    else
                    {
                        Targets.Add(addressProbe);
                        hostResult.AddressProbes.Add(addressProbe);
                    }
                }


                Hosts.Add(hostResult);
            }
        }
    }
}