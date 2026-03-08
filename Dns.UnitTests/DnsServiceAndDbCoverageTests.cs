using System;
using System.Collections.Generic;
using System.IO;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dns;
using Dns.Cli.Handlers;
using Dns.Config;
using Dns.Contracts;
using Dns.Db.Configuration;
using Dns.Db.Contexts;
using Dns.Db.Extensions;
using Dns.Db.Models.EntityFramework;
using Dns.Db.Repositories;
using Dns.Models;
using Dns.Services;
using Dns.ZoneProvider;
using Dns.ZoneProvider.Bind;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using DnsZone = Dns.Models.Zone;

namespace Dns.UnitTests;

public sealed class DnsServiceAndDbCoverageTests
{
	[Fact]
	public async Task DnsService_StartAsync_InitializesProvidersAndStartsServer()
	{
		var provider = new DnsServiceTestZoneProvider(Substitute.For<IDnsResolver>());
		var services = new ServiceCollection()
			.AddSingleton(provider)
			.BuildServiceProvider();
		var dnsServer = Substitute.For<IDnsServer>();
		dnsServer.Start(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var options = Options.Create(new ServerOptions
		{
			Zones =
			[
				new ZoneOptions
				{
					Name = "example.com",
					Provider = typeof(DnsServiceTestZoneProvider).FullName,
				}
			],
		});

		var service = new DnsService(services, options, dnsServer);
		await service.StartAsync(CancellationToken.None);

		Assert.True(provider.Initialized);
		Assert.True(provider.Started);
		Assert.Single(service.Resolvers);
		dnsServer.Received(1).Initialize(Arg.Any<List<IDnsResolver>>());
		await dnsServer.Received(1).Start(Arg.Any<CancellationToken>());

		await service.StopAsync(new CancellationToken(true));
	}

	[Fact]
	public async Task DnsService_ImportActiveBindZonesToDatabaseAndDisable_SuccessPath()
	{
		var zoneFile = WriteZoneFile();
		try
		{
			var resolver = Substitute.For<IDnsResolver>();
			var bindProvider = new DnsServiceTestBindZoneProvider(resolver);
			var zoneRepository = Substitute.For<IZoneRepository>();
			zoneRepository.UpsertZone(Arg.Any<Dns.Db.Models.EntityFramework.Zone>(), true)
				.Returns(new Dns.Db.Models.EntityFramework.Zone
				{
					Id = 42,
					Suffix = "example.com",
					Serial = 2024010101,
					Records = [new Dns.Db.Models.EntityFramework.ZoneRecord { Host = "www", Data = "192.0.2.10" }],
				});

			var services = new ServiceCollection()
				.AddSingleton(bindProvider)
				.AddScoped(_ => zoneRepository)
				.BuildServiceProvider();
			var dnsServer = Substitute.For<IDnsServer>();
			dnsServer.Start(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

			var options = Options.Create(new ServerOptions
			{
				Zones =
				[
					new ZoneOptions
					{
						Name = "example.com",
						Provider = typeof(DnsServiceTestBindZoneProvider).FullName,
						ProviderSettings = new FileWatcherZoneProviderSettings { FileName = zoneFile },
					}
				],
			});

			var service = new DnsService(services, options, dnsServer);
			await service.StartAsync(CancellationToken.None);

			var result = await service.ImportActiveBindZonesToDatabaseAndDisableAsync(true, true);

			Assert.Equal(1, result.ImportedCount);
			Assert.Equal(1, result.DisabledCount);
			Assert.Equal(0, result.FailedCount);
			Assert.Single(result.Items);
			Assert.True(result.Items[0].Imported);
			Assert.True(result.Items[0].Disabled);
			Assert.Equal(42, result.Items[0].ZoneId);
			resolver.Received(1).OnNext(Arg.Any<List<DnsZone>>());
			Assert.True(bindProvider.WasDisposed);
		}
		finally
		{
			File.Delete(zoneFile);
		}
	}

	[Fact]
	public async Task DnsService_ImportActiveBindZonesToDatabaseAndDisable_FailurePath()
	{
		var resolver = Substitute.For<IDnsResolver>();
		var bindProvider = new DnsServiceTestBindZoneProvider(resolver);
		var zoneRepository = Substitute.For<IZoneRepository>();
		var services = new ServiceCollection()
			.AddSingleton(bindProvider)
			.AddScoped(_ => zoneRepository)
			.BuildServiceProvider();
		var dnsServer = Substitute.For<IDnsServer>();
		dnsServer.Start(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var options = Options.Create(new ServerOptions
		{
			Zones =
			[
				new ZoneOptions
				{
					Name = "example.com",
					Provider = typeof(DnsServiceTestBindZoneProvider).FullName,
				},
			],
		});

		var service = new DnsService(services, options, dnsServer);
		await service.StartAsync(CancellationToken.None);

		var result = await service.ImportActiveBindZonesToDatabaseAndDisableAsync();

		Assert.Equal(0, result.ImportedCount);
		Assert.Equal(0, result.DisabledCount);
		Assert.Equal(1, result.FailedCount);
		Assert.Contains("missing file watcher settings", result.Items[0].Error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void BindZoneImportBatchResult_Defaults_AreInitialized()
	{
		var result = new BindZoneImportBatchResult();
		var item = new BindZoneImportBatchItem();

		Assert.NotNull(result.Items);
		Assert.Empty(result.Items);
		Assert.Equal(string.Empty, item.ZoneSuffix);
		Assert.Equal(string.Empty, item.FileName);
		Assert.Equal(string.Empty, item.Error);
	}

	[Fact]
	public void JwtTokenHandler_GenerateJwtToken_ContainsExpectedClaims()
	{
		var handler = new JwtTokenHandler(
			Options.Create(
				new ServerOptions { WebServer = new WebServerOptions { JwtSecretKey = "this-is-a-long-enough-secret-key" } }
			)
		);
		var token = handler.GenerateJwtToken(new User { Id = 7, Account = "admin" });
		var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

		Assert.Contains(jwt.Claims, c => c.Type == "nameid" && c.Value == "7");
		Assert.Contains(jwt.Claims, c => c.Type == "unique_name" && c.Value == "admin");
	}

	[Fact]
	public void AddDatabaseDependencies_RegistersExpectedServices()
	{
		var services = new ServiceCollection();
		services.AddDatabaseDependencies(new DatabaseSettings { SQLiteDefault = "Data Source=:memory:" });

		Assert.Contains(services, s => s.ServiceType == typeof(DnsServerDbContext));
		Assert.Contains(services, s => s.ServiceType == typeof(IUserRepository) && s.ImplementationType == typeof(UserRepository));
		Assert.Contains(services, s => s.ServiceType == typeof(IZoneRepository) && s.ImplementationType == typeof(ZoneRepository));
	}

	[Fact]
	public async Task UserRepository_VerifyAccount_RehashesWhenNeeded_AndSupportsQueries()
	{
		using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<DnsServerDbContext>()
			.UseSqlite(connection)
			.Options;
		using var context = new DnsServerDbContext(options);
		await context.Database.EnsureCreatedAsync();

		var seeded = new User
		{
			Account = "admin",
			Password = "old-hash",
			Activated = true,
			AdminLevel = 1,
		};
		context.Users.Add(seeded);
		await context.SaveChangesAsync();

		var hasher = Substitute.For<IPasswordHasher<User>>();
		hasher.VerifyHashedPassword(Arg.Any<User>(), Arg.Any<string>(), "pw")
			.Returns(PasswordVerificationResult.SuccessRehashNeeded);
		hasher.HashPassword(Arg.Any<User>(), "pw").Returns("new-hash");

		var repo = new UserRepository(Substitute.For<ILogger<UserRepository>>(), context, hasher);

		var verified = repo.VerifyAccount("admin", "pw", out var user);
		Assert.True(verified);
		Assert.NotNull(user);

		var reloaded = await context.Users.FirstAsync();
		Assert.Equal("new-hash", reloaded.Password);

		var users = await repo.GetUsers();
		Assert.Single(users);

		var fetched = await repo.GetUser("admin");
		Assert.Equal("admin", fetched.Account);

		fetched.AdminLevel = 2;
		await repo.UpdateUser(fetched);
		Assert.Equal((byte)2, (await context.Users.FirstAsync()).AdminLevel);
	}

	[Fact]
	public async Task UserRepository_VerifyAccount_RejectsInvalidInputsAndInactiveAccount()
	{
		using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();
		var options = new DbContextOptionsBuilder<DnsServerDbContext>().UseSqlite(connection).Options;
		using var context = new DnsServerDbContext(options);
		await context.Database.EnsureCreatedAsync();

		context.Users.Add(new User { Account = "inactive", Password = "hash", Activated = false });
		await context.SaveChangesAsync();

		var repo = new UserRepository(
			Substitute.For<ILogger<UserRepository>>(),
			context,
			Substitute.For<IPasswordHasher<User>>()
		);

		Assert.False(repo.VerifyAccount(string.Empty, "pw", out _));
		Assert.False(repo.VerifyAccount("inactive", "pw", out _));
		Assert.False(repo.VerifyAccount("missing", "pw", out _));
	}

	private static string WriteZoneFile()
	{
		var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zone");
		File.WriteAllLines(path,
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
			"www IN A 192.0.2.10",
		]);
		return path;
	}
}

public sealed class DnsServiceTestZoneProvider(IDnsResolver resolver) : IZoneProvider
{
	public bool Initialized { get; private set; }
	public bool Started { get; private set; }
	public IDnsResolver Resolver { get; } = resolver;

	public void Initialize(ZoneOptions zoneOptions) => Initialized = true;
	public void Start(CancellationToken ct) => Started = true;
	public IDisposable Subscribe(IObserver<List<DnsZone>> observer) => Substitute.For<IDisposable>();
}

public sealed class DnsServiceTestBindZoneProvider(IDnsResolver resolver)
	: BindZoneProvider(Substitute.For<ILogger<BindZoneProvider>>(), resolver)
{
	public bool WasDisposed { get; private set; }

	public override void Initialize(ZoneOptions zoneOptions)
	{
		// Skip file watcher initialization in unit tests.
	}

	public override void Start(CancellationToken ct)
	{
		// No background loop in unit tests.
	}

	public override void Dispose() => WasDisposed = true;
}
