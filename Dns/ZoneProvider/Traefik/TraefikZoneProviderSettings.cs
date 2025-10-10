using System.Text.Json.Serialization;

namespace Dns.ZoneProvider.Traefik;

public class TraefikZoneProviderSettings : ProviderSettings
{
	[JsonPropertyName("traefikUrl")]
	public string TraefikUrl           { get; set; }
	[JsonPropertyName("username")]
	public string Username             { get; set; }
	[JsonPropertyName("password")]
	public string Password             { get; set; }
	[JsonPropertyName("dockerHostInternalIp")]
	public string DockerHostInternalIp { get; set; }
}