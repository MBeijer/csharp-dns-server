using System.Text.Json.Serialization;

namespace Dns.Config;

public class WebServerOptions
{
	[JsonPropertyName("jwtSecretKey")]
	public string JwtSecretKey { get; set; }
}