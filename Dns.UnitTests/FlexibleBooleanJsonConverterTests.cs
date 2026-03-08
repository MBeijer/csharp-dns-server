using System.Text.Json;
using Dns.Config;
using Xunit;

namespace Dns.UnitTests;

public sealed class FlexibleBooleanJsonConverterTests
{
	private readonly JsonSerializerOptions _options;

	public FlexibleBooleanJsonConverterTests()
	{
		_options = new JsonSerializerOptions();
		_options.Converters.Add(new FlexibleBooleanJsonConverter());
	}

	[Fact]
	public void Read_ReadsBooleanAndStringValues()
	{
		Assert.True(JsonSerializer.Deserialize<bool>("true", _options));
		Assert.False(JsonSerializer.Deserialize<bool>("\"false\"", _options));
	}

	[Fact]
	public void Read_ThrowsForInvalidToken()
	{
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<bool>("123", _options));
	}
}
