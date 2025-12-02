using System.Collections.Generic;

namespace Dns.ZoneProvider.IPProbe;

internal class Host
{
	internal readonly List<Target>     AddressProbes = [];
	internal          string           Name             { get; set; }
	internal          AvailabilityMode AvailabilityMode { get; set; }
}