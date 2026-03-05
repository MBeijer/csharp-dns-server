using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Dns.ZoneProvider.Traefik.Models;

public class Route
{
	[JsonPropertyName("entryPoints")] public List<string> EntryPoints { get; set; }

	[JsonPropertyName("service")] public string Service { get; set; }

	[JsonPropertyName("rule")] public string Rule { get; set; }

	[JsonPropertyName("priority")] public object Priority { get; set; }

	[JsonPropertyName("status")] public string Status { get; set; }

	[JsonPropertyName("using")] public List<string> Using { get; set; }

	[JsonPropertyName("name")] public string Name { get; set; }

	[JsonPropertyName("provider")] public string Provider { get; set; }

	[JsonPropertyName("middlewares")] public List<string> Middlewares { get; set; }

	[JsonPropertyName("tls")] public Tls Tls { get; set; }
}
