using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Dns.Config;

public class ServerOptions
{
	[JsonPropertyName("zones")]
	public List<ZoneOptions>  Zones       { get; set; } = [];
	[JsonPropertyName("dnsListener")]
	public DnsListenerOptions DnsListener { get; set; }
	[JsonPropertyName("webServer")]
	public WebServerOptions   WebServer   { get; set; }
}