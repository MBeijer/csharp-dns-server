using System.Collections.Generic;
using Newtonsoft.Json;

namespace Dns.Models.Traefik;

public class Route
{
	[JsonProperty("entryPoints")]
	public List<string> EntryPoints { get; set; }

	[JsonProperty("service")]
	public string Service { get; set; }

	[JsonProperty("rule")]
	public string Rule { get; set; }

	[JsonProperty("priority")]
	public object Priority { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("using")]
	public List<string> Using { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("provider")]
	public string Provider { get; set; }

	[JsonProperty("middlewares")]
	public List<string> Middlewares { get; set; }

	[JsonProperty("tls")]
	public Tls Tls { get; set; }
}