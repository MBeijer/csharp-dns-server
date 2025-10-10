using System.Text.Json.Serialization;

namespace Dns.Config;

public class DnsListenerOptions
{
	[JsonPropertyName("port")]
	public ushort Port { get; set; }
}