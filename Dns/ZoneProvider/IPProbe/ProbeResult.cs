using System;

namespace Dns.ZoneProvider.IPProbe;

internal class ProbeResult
{
	internal bool     Available;
	internal TimeSpan Duration;
	internal DateTime StartTime;
}