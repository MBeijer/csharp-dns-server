using System;
using System.Linq;
using System.Threading.Tasks;
using Dns.Db.Contexts;
using Dns.Db.Models.EntityFramework;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Db.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Dns.UnitTests;

public sealed class ZoneRepositoryTests
{
	[Fact]
	public async Task UpsertZone_WithReplaceDisabled_AddsOnlyMissingNonSoaRecords()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DnsServerDbContext>()
					  .UseSqlite(connection)
					  .Options;

		await using var dbContext = new DnsServerDbContext(options);
		await dbContext.Database.EnsureCreatedAsync();

		var repository = new ZoneRepository(new FakeLogger<ZoneRepository>(), dbContext);

		await repository.AddZone(
			new Zone
			{
				Suffix = "example.com",
				Enabled = true,
				Records =
				[
					new ZoneRecord
					{
						Host = "@",
						Type = ResourceType.SOA,
						Class = ResourceClass.IN,
						Data = "ns1.example.com. hostmaster.example.com. 2026010101 1H 15M 1W 1D",
					},
					new ZoneRecord
					{
						Host = "www",
						Type = ResourceType.A,
						Class = ResourceClass.IN,
						Data = "192.0.2.10",
					},
				],
			}
		);

		await repository.UpsertZone(
			new Zone
			{
				Suffix = "example.com",
				Enabled = true,
				Records =
				[
					new ZoneRecord
					{
						Host = "@",
						Type = ResourceType.SOA,
						Class = ResourceClass.IN,
						Data = "ns9.example.com. hostmaster.example.com. 2026010101 1H 15M 1W 1D",
					},
					new ZoneRecord
					{
						Host = "www",
						Type = ResourceType.A,
						Class = ResourceClass.IN,
						Data = "192.0.2.10",
					},
					new ZoneRecord
					{
						Host = "mail",
						Type = ResourceType.A,
						Class = ResourceClass.IN,
						Data = "192.0.2.20",
					},
				],
			},
			replaceRecords: false
		);

		var zone = await repository.GetZone("example.com");
		Assert.NotNull(zone);
		Assert.Equal(1, zone!.Records!.Count(record => record.Type == ResourceType.SOA));
		Assert.Equal(1, zone.Records.Count(record => record.Type == ResourceType.A && record.Host == "www"));
		Assert.Equal(1, zone.Records.Count(record => record.Type == ResourceType.A && record.Host == "mail"));
	}

	[Fact]
	public async Task UpsertZone_WithReplaceDisabled_SkipsInvalidAndDuplicateRecords_CaseInsensitive()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DnsServerDbContext>()
					  .UseSqlite(connection)
					  .Options;

		await using var dbContext = new DnsServerDbContext(options);
		await dbContext.Database.EnsureCreatedAsync();

		var repository = new ZoneRepository(new FakeLogger<ZoneRepository>(), dbContext);

		await repository.AddZone(
			new Zone
			{
				Suffix = "example.com",
				Enabled = true,
				Records =
				[
					new ZoneRecord
					{
						Host = "www",
						Type = ResourceType.A,
						Class = ResourceClass.IN,
						Data = "192.0.2.10",
					},
				],
			}
		);

		await repository.UpsertZone(
			new Zone
			{
				Suffix = "example.com",
				Enabled = true,
				Records =
				[
					new ZoneRecord
					{
						Host = " WWW ",
						Type = ResourceType.A,
						Class = ResourceClass.IN,
						Data = "192.0.2.10",
					},
					new ZoneRecord
					{
						Host = "txt1",
						Type = ResourceType.TXT,
						Class = ResourceClass.IN,
						Data = "hello",
					},
					new ZoneRecord
					{
						Host = "skip-no-class",
						Type = ResourceType.TXT,
						Class = null,
						Data = "hello",
					},
					new ZoneRecord
					{
						Host = "skip-no-data",
						Type = ResourceType.TXT,
						Class = ResourceClass.IN,
						Data = " ",
					},
					new ZoneRecord
					{
						Host = "skip-no-type",
						Type = null,
						Class = ResourceClass.IN,
						Data = "hello",
					},
				],
			},
			replaceRecords: false
		);

		var zone = await repository.GetZone("example.com");
		Assert.NotNull(zone);
		Assert.Equal(1, zone!.Records!.Count(record => record.Type == ResourceType.A));
		Assert.Equal(1, zone.Records.Count(record => record.Type == ResourceType.TXT));
		Assert.DoesNotContain(zone.Records, r => r.Host == "skip-no-class");
		Assert.DoesNotContain(zone.Records, r => r.Host == "skip-no-data");
		Assert.DoesNotContain(zone.Records, r => r.Host == "skip-no-type");
	}

	[Fact]
	public async Task GetZone_ById_ReturnsZone()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DnsServerDbContext>()
					  .UseSqlite(connection)
					  .Options;

		await using var dbContext = new DnsServerDbContext(options);
		await dbContext.Database.EnsureCreatedAsync();

		var repository = new ZoneRepository(new FakeLogger<ZoneRepository>(), dbContext);
		var zone = new Zone
		{
			Suffix = "example.com",
			Enabled = true,
			Records = [],
		};
		await repository.AddZone(zone);

		var loaded = await repository.GetZone(zone.Id!.Value);
		Assert.NotNull(loaded);
		Assert.Equal(zone.Id, loaded!.Id);
	}

	[Fact]
	public async Task UpsertZone_Throws_WhenMasterDoesNotExist()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DnsServerDbContext>()
					  .UseSqlite(connection)
					  .Options;

		await using var dbContext = new DnsServerDbContext(options);
		await dbContext.Database.EnsureCreatedAsync();

		var repository = new ZoneRepository(new FakeLogger<ZoneRepository>(), dbContext);

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => repository.UpsertZone(
				new Zone
				{
					Suffix = "example.com",
					Enabled = true,
					MasterZoneId = 999,
					Records = [],
				},
				true
			)
		);
	}

	[Fact]
	public async Task UpsertZone_Throws_WhenUpdatingSlaveZoneDirectly()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DnsServerDbContext>()
					  .UseSqlite(connection)
					  .Options;

		await using var dbContext = new DnsServerDbContext(options);
		await dbContext.Database.EnsureCreatedAsync();

		var repository = new ZoneRepository(new FakeLogger<ZoneRepository>(), dbContext);

		var master = new Zone
		{
			Suffix = "master.example.com",
			Enabled = true,
			Records = [],
		};
		await repository.AddZone(master);

		var slave = new Zone
		{
			Suffix = "slave.example.com",
			MasterZoneId = master.Id,
		};
		await repository.AddZone(slave);

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => repository.UpsertZone(
				new Zone
				{
					Suffix = "slave.example.com",
					Enabled = true,
					MasterZoneId = master.Id,
					Records = [],
				},
				true
			)
		);
	}

	[Fact]
	public async Task SlaveSynchronization_RewritesCNameApexTargetsToSlaveSuffix()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DnsServerDbContext>()
					  .UseSqlite(connection)
					  .Options;

		await using var dbContext = new DnsServerDbContext(options);
		await dbContext.Database.EnsureCreatedAsync();

		var repository = new ZoneRepository(new FakeLogger<ZoneRepository>(), dbContext);

		var master = new Zone
		{
			Suffix = "master.example.com",
			Enabled = true,
			Records =
			[
				new ZoneRecord
				{
					Host = "www",
					Type = ResourceType.CNAME,
					Class = ResourceClass.IN,
					Data = "master.example.com",
				},
				new ZoneRecord
				{
					Host = "api",
					Type = ResourceType.CNAME,
					Class = ResourceClass.IN,
					Data = "@",
				},
				new ZoneRecord
				{
					Host = "external",
					Type = ResourceType.CNAME,
					Class = ResourceClass.IN,
					Data = "outside.example.net",
				},
			],
		};

		await repository.AddZone(master);

		var slave = new Zone
		{
			Suffix = "slave.example.com",
			MasterZoneId = master.Id,
		};
		await repository.AddZone(slave);

		var syncedSlave = await repository.GetZone("slave.example.com");
		Assert.NotNull(syncedSlave);

		var wwwAlias = Assert.Single(syncedSlave!.Records!, record => string.Equals(record.Host, "www", StringComparison.Ordinal));
		Assert.Equal("slave.example.com", wwwAlias.Data);

		var apiAlias = Assert.Single(syncedSlave.Records!, record => string.Equals(record.Host, "api", StringComparison.Ordinal));
		Assert.Equal("slave.example.com", apiAlias.Data);

		var externalAlias = Assert.Single(
			syncedSlave.Records!,
			record => string.Equals(record.Host, "external", StringComparison.Ordinal)
		);
		Assert.Equal("outside.example.net", externalAlias.Data);
	}

	[Fact]
	public async Task SlaveSynchronization_RewritesApexAliasesWithTrailingDotAndCaseInsensitiveMasterSuffix()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DnsServerDbContext>()
					  .UseSqlite(connection)
					  .Options;

		await using var dbContext = new DnsServerDbContext(options);
		await dbContext.Database.EnsureCreatedAsync();

		var repository = new ZoneRepository(new FakeLogger<ZoneRepository>(), dbContext);
		var master = new Zone
		{
			Suffix = "master.example.com",
			Enabled = true,
			Records =
			[
				new ZoneRecord
				{
					Host = "atdot",
					Type = ResourceType.CNAME,
					Class = ResourceClass.IN,
					Data = "@.",
				},
				new ZoneRecord
				{
					Host = "rootdot",
					Type = ResourceType.CNAME,
					Class = ResourceClass.IN,
					Data = "MASTER.EXAMPLE.COM.",
				},
			],
		};
		await repository.AddZone(master);

		var slave = new Zone
		{
			Suffix = "slave.example.com.",
			MasterZoneId = master.Id,
		};
		await repository.AddZone(slave);

		var syncedSlave = await repository.GetZone("slave.example.com.");
		Assert.NotNull(syncedSlave);

		var atdot = Assert.Single(syncedSlave!.Records!, r => r.Host == "atdot");
		Assert.Equal("slave.example.com.", atdot.Data);

		var rootdot = Assert.Single(syncedSlave.Records!, r => r.Host == "rootdot");
		Assert.Equal("slave.example.com.", rootdot.Data);
	}
}
