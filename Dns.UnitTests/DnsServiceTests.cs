using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dns;
using Dns.Config;
using Dns.Contracts;
using Dns.Db.Repositories;
using Dns.Services;
using Dns.ZoneProvider;
using Dns.ZoneProvider.Bind;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using DnsZone = Dns.Models.Zone;

namespace Dns.UnitTests;

public sealed class DnsServiceTests
{
	private readonly IDnsServer _dnsServer;

	public DnsServiceTests()
	{
		_dnsServer = Substitute.For<IDnsServer>();
		_dnsServer.Start(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
	}

	[Fact]
	public async Task StartAsync_InitializesProvidersAndStartsServer()
	{
		var provider = new DnsServiceTestZoneProvider(Substitute.For<IDnsResolver>());
		var services = new ServiceCollection().AddSingleton(provider).BuildServiceProvider();
		var options = Options.Create(new ServerOptions
		{
			Zones = [new ZoneOptions { Name = "example.com", Provider = typeof(DnsServiceTestZoneProvider).FullName }],
		});
		var target = new DnsService(services, options, _dnsServer);

		await target.StartAsync(CancellationToken.None);

		Assert.True(provider.Initialized);
		Assert.True(provider.Started);
		Assert.Single(target.Resolvers);
		_dnsServer.Received(1).Initialize(Arg.Any<List<IDnsResolver>>());
		await _dnsServer.Received(1).Start(Arg.Any<CancellationToken>());

		await target.StopAsync(new CancellationToken(true));
	}

	[Fact]
	public async Task ImportActiveBindZonesToDatabaseAndDisableAsync_SuccessPath()
	{
		var zoneFile = WriteZoneFile();
		try
		{
			var resolver = Substitute.For<IDnsResolver>();
			var bindProvider = new DnsServiceTestBindZoneProvider(resolver);
			var zoneRepository = Substitute.For<IZoneRepository>();
			zoneRepository.UpsertZone(Arg.Any<Dns.Db.Models.EntityFramework.Zone>(), true)
				.Returns(new Dns.Db.Models.EntityFramework.Zone { Id = 42, Suffix = "example.com", Serial = 2024010101, Records = [new Dns.Db.Models.EntityFramework.ZoneRecord { Host = "www", Data = "192.0.2.10" }] });

			var services = new ServiceCollection().AddSingleton(bindProvider).AddScoped(_ => zoneRepository).BuildServiceProvider();
			var options = Options.Create(new ServerOptions
			{
				Zones = [new ZoneOptions { Name = "example.com", Provider = typeof(DnsServiceTestBindZoneProvider).FullName, ProviderSettings = new FileWatcherZoneProviderSettings { FileName = zoneFile } }],
			});
			var target = new DnsService(services, options, _dnsServer);
			await target.StartAsync(CancellationToken.None);

			var result = await target.ImportActiveBindZonesToDatabaseAndDisableAsync(true, true);

			Assert.Equal(1, result.ImportedCount);
			Assert.Equal(1, result.DisabledCount);
			Assert.Equal(0, result.FailedCount);
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
	public async Task ImportActiveBindZonesToDatabaseAndDisableAsync_FailurePath()
	{
		var resolver = Substitute.For<IDnsResolver>();
		var bindProvider = new DnsServiceTestBindZoneProvider(resolver);
		var zoneRepository = Substitute.For<IZoneRepository>();
		var services = new ServiceCollection().AddSingleton(bindProvider).AddScoped(_ => zoneRepository).BuildServiceProvider();
		var options = Options.Create(new ServerOptions
		{
			Zones = [new ZoneOptions { Name = "example.com", Provider = typeof(DnsServiceTestBindZoneProvider).FullName }],
		});
		var target = new DnsService(services, options, _dnsServer);
		await target.StartAsync(CancellationToken.None);

		var result = await target.ImportActiveBindZonesToDatabaseAndDisableAsync();

		Assert.Equal(1, result.FailedCount);
		Assert.Contains("missing file watcher settings", result.Items[0].Error, StringComparison.OrdinalIgnoreCase);
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

	private sealed class DnsServiceTestZoneProvider(IDnsResolver resolver) : IZoneProvider
	{
		public bool Initialized { get; private set; }
		public bool Started { get; private set; }
		public IDnsResolver Resolver { get; } = resolver;

		public void Initialize(ZoneOptions zoneOptions) => Initialized = true;
		public void Start(CancellationToken ct) => Started = true;
		public IDisposable Subscribe(IObserver<List<DnsZone>> observer) => Substitute.For<IDisposable>();
	}

	private sealed class DnsServiceTestBindZoneProvider(IDnsResolver resolver)
		: BindZoneProvider(Substitute.For<ILogger<BindZoneProvider>>(), resolver)
	{
		public bool WasDisposed { get; private set; }

		public override void Initialize(ZoneOptions zoneOptions)
		{
		}

		public override void Start(CancellationToken ct)
		{
		}

		public override void Dispose() => WasDisposed = true;
	}
}