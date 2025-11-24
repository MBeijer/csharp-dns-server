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
        var      zone     = provider.GenerateZone();

        Assert.NotNull(zone);
        Assert.Equal("example.com", zone.Suffix);
        Assert.Equal(0u, zone.Serial);

        var filteredRecords = zone.Records.Where(record => record.Host == "www.example.com" && record.Type == ResourceType.A);

        var wwwA = Assert.Single(filteredRecords);
        Assert.Equal("192.0.2.10", Assert.Single(wwwA.Addresses));

        filteredRecords = zone.Records.Where(record => record.Host == "www.example.com" && record.Type == ResourceType.AAAA);
        var wwwAaaa = Assert.Single(filteredRecords);
        Assert.Equal("2001:db8::10", Assert.Single(wwwAaaa.Addresses));

        filteredRecords = zone.Records.Where(record => record.Host == "example.com" && record.Type == ResourceType.A);

        var apex = Assert.Single(filteredRecords);
        Assert.Contains("192.0.2.20", apex.Addresses);

        filteredRecords = zone.Records.Where(record => record.Host == "api.example.com");
        var api = Assert.Single(filteredRecords);
        Assert.Equal("192.0.2.30", Assert.Single(api.Addresses));
    }

    [Fact]
    public void GenerateZone_InvalidZoneReturnsNull()
    {
        var zoneFile = Path.Combine(TestProjectPaths.TestDataDirectory, "Bind", "invalid_missing_ttl.zone");

        using var provider = CreateProvider(zoneFile);
        var      zone     = provider.GenerateZone();

        Assert.Null(zone);
    }

    [Fact]
    public void GenerateZone_ReturnsNullWhenCNameConflictsWithAddress()
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

            Assert.Null(zone);
        }
        finally
        {
            File.Delete(tempZone);
        }
    }

    private BindZoneProvider CreateProvider(string zoneFile)
    {
        var provider = new BindZoneProvider(new FakeLogger<BindZoneProvider>(), new SmartZoneResolver(new FakeLogger<SmartZoneResolver>()));
        provider.Initialize(new()
        {
            Name = "example.com",
            ProviderSettings = new FileWatcherZoneProviderSettings
            {
                FileName = zoneFile,
            },
        });
        return provider;
    }

    private string WriteTempZoneFile(IEnumerable<string> lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, lines);
        return path;
    }
}