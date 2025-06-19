using System.Collections.Generic;

namespace Dns.Config;

public class ServerOptions
{
	public List<ZoneOptions>  Zones       { get; set; } = [];
	public DnsListenerOptions DnsListener { get; set; }
	public WebServerOptions   WebServer   { get; set; }
}