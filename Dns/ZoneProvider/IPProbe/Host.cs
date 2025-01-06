using System.Collections.Generic;

namespace Dns.ZoneProvider.IPProbe;

internal class Host
{
    internal string           Name             { get; set; }
    internal AvailabilityMode AvailabilityMode { get; set; }
    internal List<Target>     AddressProbes = new();
}