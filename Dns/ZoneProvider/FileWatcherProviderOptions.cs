using System.Text.Json.Serialization;

namespace Dns.ZoneProvider;

public class FileWatcherZoneProviderSettings : ProviderSettings
{
	[JsonPropertyName("fileName")]
    public string FileName { get; set; }
}