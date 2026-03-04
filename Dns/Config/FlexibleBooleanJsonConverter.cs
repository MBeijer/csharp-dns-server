using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dns.Config;

public sealed class FlexibleBooleanJsonConverter : JsonConverter<bool>
{
	public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return reader.TokenType switch
		{
			JsonTokenType.True => true,
			JsonTokenType.False => false,
			JsonTokenType.String when bool.TryParse(reader.GetString(), out var value) => value,
			_ => throw new JsonException("Expected a boolean value or a boolean string.")
		};
	}

	public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
		writer.WriteBooleanValue(value);
}
