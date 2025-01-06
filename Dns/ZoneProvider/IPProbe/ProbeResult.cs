using System;

namespace Dns.ZoneProvider.IPProbe;

internal class ProbeResult
{
    internal DateTime StartTime;
    internal TimeSpan Duration;
    internal bool     Available;
}