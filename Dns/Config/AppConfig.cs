using System.Text.Json.Serialization;

namespace Dns.Config;

public class AppConfig
{
	[JsonPropertyName("server")] public ServerOptions Server { get; set; }
}