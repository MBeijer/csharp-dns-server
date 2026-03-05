using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dns.Config;

public sealed class FlexibleStringListJsonConverter : JsonConverter<List<string>>
{
	public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.Null:
				return [];
			case JsonTokenType.String:
			{
				var value = reader.GetString();
				if (string.IsNullOrWhiteSpace(value)) return [];

				return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
				            .Select(item => item.Trim())
				            .Where(item => item.Length > 0)
				            .ToList();
			}
			case JsonTokenType.StartArray:
			{
				var items = new List<string>();
				while (reader.Read())
				{
					if (reader.TokenType == JsonTokenType.EndArray) return items;
					if (reader.TokenType != JsonTokenType.String)
						throw new JsonException("Expected only string elements in array.");

					var value = reader.GetString();
					if (!string.IsNullOrWhiteSpace(value)) items.Add(value.Trim());
				}

				throw new JsonException("Unterminated array while reading string list.");
			}
			default:
				throw new JsonException("Expected string, string array, or null for list value.");
		}
	}

	public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		foreach (var item in value ?? [])
			writer.WriteStringValue(item);
		writer.WriteEndArray();
	}
}
