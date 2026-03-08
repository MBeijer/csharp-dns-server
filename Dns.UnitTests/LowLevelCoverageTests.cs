using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Dns.Config;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Extensions;
using Dns.Models;
using Dns.Models.Dns.Packets;
using Dns.Utility;
using Xunit;

namespace Dns.UnitTests;

public sealed class LowLevelCoverageTests
{
	[Fact]
	public void FlexibleBooleanJsonConverter_ReadsBooleanAndStringValues()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new FlexibleBooleanJsonConverter());

		Assert.True(JsonSerializer.Deserialize<bool>("true", options));
		Assert.False(JsonSerializer.Deserialize<bool>("\"false\"", options));
	}

	[Fact]
	public void FlexibleBooleanJsonConverter_ThrowsForInvalidToken()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new FlexibleBooleanJsonConverter());

		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<bool>("123", options));
	}

	[Fact]
	public void FlexibleStringListJsonConverter_ReadsStringAndArrayAndNull()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new FlexibleStringListJsonConverter());

		var fromString = JsonSerializer.Deserialize<List<string>>("\"a,b, c\"", options);
		var fromArray = JsonSerializer.Deserialize<List<string>>("[\"x\",\" y \" ]", options);
		var fromNull = JsonSerializer.Deserialize<List<string>>("null", options);

		Assert.Equal(["a", "b", "c"], fromString);
		Assert.Equal(["x", "y"], fromArray);
		Assert.Null(fromNull);
	}

	[Fact]
	public void FlexibleStringListJsonConverter_ThrowsForInvalidArrayElement()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new FlexibleStringListJsonConverter());

		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<List<string>>("[1]", options));
	}

	[Fact]
	public void FlexibleStringListJsonConverter_WritesArray()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new FlexibleStringListJsonConverter());

		var json = JsonSerializer.Serialize(new List<string> { "a", "b" }, options);

		Assert.Equal("[\"a\",\"b\"]", json);
	}

	[Fact]
	public void StringExtensions_GetResourceBytes_ProducesDnsLabelFormat()
	{
		var bytes = "www.example".GetResourceBytes();

		Assert.Equal(3, bytes[0]);
		Assert.Equal((byte)'w', bytes[1]);
		Assert.Equal(7, bytes[4]);
		Assert.Equal(0, bytes[12]);
	}

	[Fact]
	public void StringExtensions_GetBytes_UsesProvidedEncoding()
	{
		var bytes = "abc".GetBytes(Encoding.UTF8);

		Assert.Equal(new byte[] { 97, 98, 99 }, bytes);
	}

	[Fact]
	public void NumberExtensions_Ip_ReturnsStringAddress()
	{
		var value = 0x0100007FL;

		var ip = value.IP();

		Assert.Equal("127.0.0.1", ip);
	}

	[Fact]
	public void DnsRequestKey_IsCaseInsensitiveAndHasStableString()
	{
		var keyA = new DnsRequestKey(12, ResourceClass.IN, ResourceType.A, "WWW.Example.Com");
		var keyB = new DnsRequestKey(12, ResourceClass.IN, ResourceType.A, "www.example.com");

		Assert.Equal(keyA, keyB);
		Assert.True(keyA == keyB);
		Assert.False(keyA != keyB);
		Assert.Contains("12|IN|A|", keyA.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void DnsRequestKey_FromDnsMessage_UsesFirstQuestion()
	{
		var message = new DnsMessage
		{
			QueryIdentifier = 99,
			QuestionCount = 1,
			Questions = [new Question("example.com", ResourceType.AAAA, ResourceClass.IN)],
		};

		var key = new DnsRequestKey(message);

		Assert.Equal((ushort)99, key.QueryId);
		Assert.Equal(ResourceType.AAAA, key.Type);
	}

	[Fact]
	public void DnsZoneLookupKey_IsCaseInsensitive()
	{
		var keyA = new DnsZoneLookupKey("WWW", ResourceClass.IN, ResourceType.CNAME);
		var keyB = new DnsZoneLookupKey("www", ResourceClass.IN, ResourceType.CNAME);

		Assert.Equal(keyA, keyB);
		Assert.Equal(keyA.GetHashCode(), keyB.GetHashCode());
	}

	[Fact]
	public void SmartAddressDispenser_ReturnsRotatingAddresses()
	{
		var record = new ZoneRecord
		{
			Host = "www",
			Addresses = ["192.0.2.1", "192.0.2.2", "192.0.2.3"],
		};
		var dispenser = new SmartAddressDispenser(record, 2);

		var first = dispenser.GetAddresses().Select(ip => ip.ToString()).ToList();
		var second = dispenser.GetAddresses().Select(ip => ip.ToString()).ToList();

		Assert.Equal(["192.0.2.1", "192.0.2.2"], first);
		Assert.Equal(["192.0.2.2", "192.0.2.3"], second);
	}

	[Fact]
	public void SmartAddressDispenser_DumpHtmlAndGetObject_Work()
	{
		var record = new ZoneRecord { Host = "www", Addresses = ["192.0.2.1"] };
		var dispenser = new SmartAddressDispenser(record);
		using var writer = new StringWriter();

		dispenser.DumpHtml(writer);
		var value = dispenser.GetObject();

		Assert.Contains("Sequence", writer.ToString(), StringComparison.Ordinal);
		Assert.Same(record.Addresses, value);
	}

	[Fact]
	public void CsvParser_Create_ThrowsOnInvalidArguments()
	{
		Assert.Throws<ArgumentNullException>(() => CsvParser.Create(null));
		Assert.Throws<FileNotFoundException>(() => CsvParser.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv")));
	}

	[Fact]
	public void CsvParser_ParsesFieldDeclarationsCommentsAndRows()
	{
		var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
		File.WriteAllLines(path,
		[
			"#Fields: host,addr",
			"example,192.0.2.1",
			"; comment",
			"",
			"api,192.0.2.2",
		]);

		try
		{
			var parser = CsvParser.Create(path);
			var rows = parser.Rows.ToList();

			Assert.Equal(["host", "addr"], parser.Fields);
			Assert.Equal("example", rows[0][0]);
			Assert.Equal("192.0.2.1", rows[0]["addr"]);
			Assert.Equal("api", rows[1]["host"]);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void ConfigOptions_CanBeInitialized()
	{
		var zoneTransfer = new ZoneTransferOptions { Enabled = true, InjectedNsAddress = "192.0.2.10" };
		var listener = new DnsListenerOptions { Port = 53, TcpPort = 5335 };
		var zone = new ZoneOptions { Name = "example", Provider = "database" };
		var web = new WebServerOptions { JwtSecretKey = "secret" };
		var server = new ServerOptions { DnsListener = listener, WebServer = web, Zones = [zone], ZoneTransfer = zoneTransfer };
		var app = new AppConfig { Server = server };

		Assert.Equal((ushort)53, app.Server.DnsListener.Port);
		Assert.Equal("database", app.Server.Zones[0].Provider);
		Assert.Equal("secret", app.Server.WebServer.JwtSecretKey);
		Assert.True(app.Server.ZoneTransfer.Enabled);
	}
}
