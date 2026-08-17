using System.Linq;
using Dns.Cli.Models.Dto;
using Dns.Db.Models.EntityFramework.Enums;
using Xunit;

namespace Dns.UnitTests;

public sealed class DtoMappingsTests
{
	[Fact]
	public void ToEntity_ExpandsRelativeDomainNameDataAgainstZoneSuffix()
	{
		var entity = new ZoneDto
		{
			Suffix = "example.com.",
			Records =
			[
				new() { Host = "dev", Type         = ResourceType.CNAME, Data = "server1" },
				new() { Host = "nested", Type      = ResourceType.CNAME, Data = "server1.subdomain" },
				new() { Host = "external", Type    = ResourceType.CNAME, Data = "server1.other.test." },
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

		Assert.Equal("server1.example.com.", entity.Records!.Single(record => record.Host == "dev").Data);
		Assert.Equal("server1.subdomain.example.com.", entity.Records.Single(record => record.Host == "nested").Data);
		Assert.Equal("server1.other.test.", entity.Records.Single(record => record.Host == "external").Data);
		Assert.Equal(
			"ns1.example.com.",
			entity.Records.Single(record => record.Host == "@" && record.Type == ResourceType.NS).Data
		);
		Assert.Equal("ns1.other.test.", entity.Records.Single(record => record.Host == "external-ns").Data);
		Assert.Equal("10 mx.backup.example.com.", entity.Records.Single(record => record.Host == "mail").Data);
		Assert.Equal("host.reverse.example.com.", entity.Records.Single(record => record.Host == "pointer").Data);
		Assert.Equal(
			"ns.primary.example.com. hostmaster.mail.example.com. 2026081701 3600 600 1209600 300",
			entity.Records.Single(record => record.Type == ResourceType.SOA).Data
		);
		Assert.Equal("192.0.2.10", entity.Records.Single(record => record.Host == "address").Data);
	}
}