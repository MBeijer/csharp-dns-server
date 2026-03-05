// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="BindZoneProviderTests.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.UnitTests.Integration;
using Dns.ZoneProvider;
using Dns.ZoneProvider.Bind;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Dns.UnitTests;

public class BindZoneProviderTests
{
	[Fact]
	public void GenerateZone_ReturnsZoneRecordsFromBindFile()
	{
		var zoneFile = Path.Combine(TestProjectPaths.TestDataDirectory, "Bind", "simple.zone");

		using var provider = CreateProvider(zoneFile);
		var       zone     = provider.GenerateZone();

		Assert.NotNull(zone);
		Assert.Equal("example.com", zone.Suffix);
		Assert.Equal(2024010101u, zone.Serial);

		var filteredRecords = zone.Records.Where(record => record.Host == "www" && record.Type == ResourceType.A);

		var wwwA = Assert.Single(filteredRecords);
		Assert.Equal("192.0.2.10", Assert.Single(wwwA.Addresses));

		filteredRecords = zone.Records.Where(record => record.Host == "www" && record.Type == ResourceType.AAAA);
		var wwwAaaa = Assert.Single(filteredRecords);
		Assert.Equal("2001:db8::10", Assert.Single(wwwAaaa.Addresses));

		filteredRecords = zone.Records.Where(record => record.Host == "" && record.Type == ResourceType.A);

		var apex = Assert.Single(filteredRecords);
		Assert.Contains("192.0.2.20", apex.Addresses);

		filteredRecords = zone.Records.Where(record => record.Host == "api" && record.Type == ResourceType.A);
		var api = Assert.Single(filteredRecords);
		Assert.Equal("192.0.2.30", Assert.Single(api.Addresses));
	}

	[Fact]
	public void GenerateZone_MissingTtlDirectiveUsesDefaultTtl()
	{
		var zoneFile = Path.Combine(TestProjectPaths.TestDataDirectory, "Bind", "invalid_missing_ttl.zone");

		using var provider = CreateProvider(zoneFile);
		var       zone     = provider.GenerateZone();

		Assert.NotNull(zone);
		Assert.Equal("example.com", zone.Suffix);
		Assert.Equal(2024010101u, zone.Serial);

		var wwwA = Assert.Single(
			zone.Records,
			record => record.Host == "www" && record.Type == ResourceType.A
		);
		Assert.Equal("10.0.0.1", Assert.Single(wwwA.Addresses));
	}

	[Fact]
	public void GenerateZone_AllowsCNameAlongsideAddressRecord()
	{
		var tempZone = WriteTempZoneFile(
			[
				"$TTL 1h",
				"$ORIGIN example.com.",
				"@ IN SOA ns1.example.com. hostmaster.example.com. (",
				"    2024010101",
				"    7200",
				"    3600",
				"    1209600",
				"    3600 )",
				"@ IN NS ns1.example.com.",
				"www IN CNAME api",
				"www IN A 192.0.2.40",
				"api IN A 192.0.2.50",
			]
		);

		try
		{
			using var provider = CreateProvider(tempZone);
			var       zone     = provider.GenerateZone();

			Assert.NotNull(zone);

			var cname = Assert.Single(zone.Records, record => record.Host == "www" && record.Type == ResourceType.CNAME);
			Assert.Equal("api.example.com", Assert.Single(cname.Addresses));

			var aRecord = Assert.Single(zone.Records, record => record.Host == "www" && record.Type == ResourceType.A);
			Assert.Equal("192.0.2.40", Assert.Single(aRecord.Addresses));
		}
		finally
		{
			File.Delete(tempZone);
		}
	}

	[Fact]
	public void GenerateZone_ReturnsPtrRecordFromReverseZone()
	{
		var tempZone = WriteTempZoneFile(
			[
				"$TTL 1h",
				"$ORIGIN 2.0.192.in-addr.arpa.",
				"@ IN SOA ns1.example.com. hostmaster.example.com. (",
				"    2024010101",
				"    7200",
				"    3600",
				"    1209600",
				"    3600 )",
				"@ IN NS ns1.example.com.",
				"10 IN PTR host1.example.com.",
			]
		);

		try
		{
			using var provider = CreateProvider(tempZone, "2.0.192.in-addr.arpa");
			var       zone     = provider.GenerateZone();

			Assert.NotNull(zone);
			var ptr = Assert.Single(zone.Records, record => record.Host == "10" && record.Type == ResourceType.PTR);
			Assert.Equal("host1.example.com", Assert.Single(ptr.Addresses));
		}
		finally
		{
			File.Delete(tempZone);
		}
	}

	[Fact]
	public void GenerateZone_FirstRecordWithImplicitOwnerUsesCurrentOrigin()
	{
		var tempZone = WriteTempZoneFile(
			[
				"$ORIGIN example.com.",
				"    IN SOA ns1.example.com. hostmaster.example.com. (",
				"        2024010101",
				"        7200",
				"        3600",
				"        1209600",
				"        3600 )",
				"    IN NS ns1.example.com.",
				"www IN A 192.0.2.10",
			]
		);

		try
		{
			using var provider = CreateProvider(tempZone);
			var       zone     = provider.GenerateZone();

			Assert.NotNull(zone);
			Assert.Equal(2024010101u, zone.Serial);
			var apexSoa = Assert.Single(zone.Records, record => record.Host == "" && record.Type == ResourceType.SOA);
			Assert.Equal("ns1.example.com", apexSoa.Addresses[0]);
		}
		finally
		{
			File.Delete(tempZone);
		}
	}

	private BindZoneProvider CreateProvider(string zoneFile, string zoneName = "example.com")
	{
		var provider = new BindZoneProvider(
			new FakeLogger<BindZoneProvider>(),
			new SmartZoneResolver(new FakeLogger<SmartZoneResolver>())
		);
		provider.Initialize(
			new()
			{
				Name             = zoneName,
				ProviderSettings = new FileWatcherZoneProviderSettings { FileName = zoneFile },
			}
		);
		return provider;
	}

	private static string WriteTempZoneFile(IEnumerable<string> lines)
	{
		var path = Path.GetTempFileName();
		File.WriteAllLines(path, lines);
		return path;
	}
}
