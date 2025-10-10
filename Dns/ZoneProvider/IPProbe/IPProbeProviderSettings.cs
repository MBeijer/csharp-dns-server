using System.Text.Json.Serialization;

namespace Dns.ZoneProvider.IPProbe;

public class IPProbeProviderSettings : ProviderSettings
{
	[JsonPropertyName("pollingIntervalSeconds")]
    public ushort        PollingIntervalSeconds { get; set; }
	[JsonPropertyName("hosts")]
    public HostOptions[] Hosts                  { get; set; }
}