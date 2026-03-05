using System.Text.Json.Serialization;

namespace Dns.Config;

public class WebServerOptions
{
	[JsonPropertyName("JwtSecretKey")] public string JwtSecretKey { get; set; }
}