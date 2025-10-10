using System.Text.Json.Serialization;
using Dns.ZoneProvider;

namespace Dns.Config;

public class ZoneOptions
{
	[JsonPropertyName("name")]
	public string           Name             { get; set; }
	[JsonPropertyName("provider")]
	public string           Provider         { get; set; }
	[JsonPropertyName("providerSettings")]
	public ProviderSettings ProviderSettings { get; set; }
}