using System.Collections.Generic;
using System.Text.Json;
using Dns.Config;
using Xunit;

namespace Dns.UnitTests;

public sealed class FlexibleStringListJsonConverterTests
{
	private readonly JsonSerializerOptions _options;

	public FlexibleStringListJsonConverterTests()
	{
		_options = new JsonSerializerOptions();
		_options.Converters.Add(new FlexibleStringListJsonConverter());
	}

	[Fact]
	public void Read_ReadsStringAndArrayAndNull()
	{
		var fromString = JsonSerializer.Deserialize<List<string>>("\"a,b, c\"", _options);
		var fromArray = JsonSerializer.Deserialize<List<string>>("[\"x\",\" y \" ]", _options);
		var fromNull = JsonSerializer.Deserialize<List<string>>("null", _options);

		Assert.Equal(["a", "b", "c"], fromString);
		Assert.Equal(["x", "y"], fromArray);
		Assert.Null(fromNull);
	}

	[Fact]
	public void Read_ThrowsForInvalidArrayElement()
	{
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<List<string>>("[1]", _options));
	}

	[Fact]
	public void Write_WritesArray()
	{
		var json = JsonSerializer.Serialize(new List<string> { "a", "b" }, _options);
		Assert.Equal("[\"a\",\"b\"]", json);
	}
}
