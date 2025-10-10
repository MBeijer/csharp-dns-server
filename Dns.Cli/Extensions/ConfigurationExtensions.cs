using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Dns.Cli.Extensions;

public static class ConfigurationExtensions
{
	private static JsonNode ToJsonNode(this IConfiguration section)
	{
		var children = section.GetChildren().ToList();
		if (children.Count == 0)
		{
			// Leaf value (may be null)
			var val = (section as IConfigurationSection)?.Value;
			return val is null ? JsonValue.Create((string?)null)! : JsonValue.Create(val)!;
		}

		// Array if keys are 0..N-1
		if (children.All(c => int.TryParse(c.Key, out _)))
		{
			var arr = new JsonArray();
			foreach (var child in children.OrderBy(c => int.Parse(c.Key)))
				arr.Add(ToJsonNode(child));
			return arr;
		}

		// Object
		var obj = new JsonObject();
		foreach (var child in children)
			obj[child.Key] = ToJsonNode(child);
		return obj;
	}

	public static JsonElement ReadJsonElement(this IConfiguration section)
		=> JsonSerializer.SerializeToElement(ToJsonNode(section));
}