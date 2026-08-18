using System.Linq;
using Dns.Cli.Models.Dto;
using Dns.Db.Models.EntityFramework.Enums;
using Xunit;

namespace Dns.UnitTests;

public sealed class DtoMappingsTests
{
	[Theory]
	[InlineData(ResourceType.A, "192.0.2.10")]
	[InlineData(ResourceType.AAAA, "2001:db8::10")]
	[InlineData(ResourceType.CNAME, "www")]
	[InlineData(ResourceType.NS, "ns1")]
	[InlineData(ResourceType.MX, "10 mail")]
	[InlineData(ResourceType.TXT, "v=spf1 -all")]
	[InlineData(ResourceType.PTR, "host.reverse")]
	[InlineData(ResourceType.SRV, "10 5 443 service")]
	[InlineData(ResourceType.SOA, "ns1 hostmaster 1 3600 600 1209600 300")]
	public void ToEntity_PreservesApexOwnerAndDataForEverySpaRecordType(ResourceType type, string data)
	{
		var entity = new ZoneRecordDto { Host = "@", Type = type, Data = data }.ToEntity();

		Assert.Equal("@", entity.Host);
		Assert.Equal(data, entity.Data);
	}

	[Fact]
	public void ToEntity_PreservesDomainNameDataForSpaRoundTrips()
	{
		var entity = new ZoneDto
		{
			Suffix = "example.com.",
			Records =
			[
				new() { Host = "dev", Type         = ResourceType.CNAME, Data = "server1" },
				new() { Host = "nested", Type      = ResourceType.CNAME, Data = "server1.subdomain" },
				new() { Host = "external", Type    = ResourceType.CNAME, Data = "server1.other.test." },
				new() { Host = "@", Type           = ResourceType.CNAME, Data = "apex-target" },
				new() { Host = "@", Type           = ResourceType.NS, Data    = "ns1" },
				new() { Host = "external-ns", Type = ResourceType.NS, Data    = "ns1.other.test." },
				new() { Host = "mail", Type        = ResourceType.MX, Data    = "10 mx.backup" },
				new() { Host = "pointer", Type     = ResourceType.PTR, Data   = "host.reverse" },
				new()
				{
					Host = "@",
					Type = ResourceType.SOA,
					Data = "ns.primary hostmaster.mail 2026081701 3600 600 1209600 300",
				},
				new() { Host = "address", Type = ResourceType.A, Data = "192.0.2.10" },
			],
		}.ToEntity();

		Assert.Equal("server1", entity.Records!.Single(record => record.Host == "dev").Data);
		Assert.Equal("server1.subdomain", entity.Records.Single(record => record.Host == "nested").Data);
		Assert.Equal("server1.other.test.", entity.Records.Single(record => record.Host == "external").Data);
		Assert.Equal(
			"apex-target",
			entity.Records.Single(record => record.Host == "@" && record.Type == ResourceType.CNAME).Data
		);
		Assert.Equal("ns1", entity.Records.Single(record => record.Host == "@" && record.Type == ResourceType.NS).Data);
		Assert.Equal("ns1.other.test.", entity.Records.Single(record => record.Host == "external-ns").Data);
		Assert.Equal("10 mx.backup", entity.Records.Single(record => record.Host == "mail").Data);
		Assert.Equal("host.reverse", entity.Records.Single(record => record.Host == "pointer").Data);
		Assert.Equal(
			"ns.primary hostmaster.mail 2026081701 3600 600 1209600 300",
			entity.Records.Single(record => record.Type == ResourceType.SOA).Data
		);
		Assert.Equal("192.0.2.10", entity.Records.Single(record => record.Host == "address").Data);
	}
}