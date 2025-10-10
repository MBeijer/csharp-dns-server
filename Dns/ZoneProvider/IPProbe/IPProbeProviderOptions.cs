namespace Dns.ZoneProvider.IPProbe;

public class IPProbeProviderOptions
{
    public ushort        PollingIntervalSeconds { get; set; }
    public HostOptions[] Hosts                  { get; set; }
}