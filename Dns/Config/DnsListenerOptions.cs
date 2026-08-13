using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Dns.Config;

public class DnsListenerOptions
{
	[JsonPropertyName("port")] public ushort Port { get; set; }

	[JsonPropertyName("tcpPort")] public ushort? TcpPort { get; set; }

	[JsonPropertyName("recursionEnabled")] public bool RecursionEnabled { get; set; }

	[JsonPropertyName("allowRecursionFrom")]
	public List<string> AllowRecursionFrom { get; set; } = [];
}