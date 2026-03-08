using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Db.Repositories;
using Dns.Models;
using Dns.ZoneProvider;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using DbZone = Dns.Db.Models.EntityFramework.Zone;
using DbZoneRecord = Dns.Db.Models.EntityFramework.ZoneRecord;

namespace Dns.UnitTests;

public sealed class DatabaseZoneProviderTests
{
	private readonly IZoneRepository _zoneRepository;
	private readonly IDnsResolver _resolver;
	private readonly DatabaseZoneProvider _target;

	public DatabaseZoneProviderTests()
	{
		_zoneRepository = Substitute.For<IZoneRepository>();
		var services = new ServiceCollection().AddScoped(_ => _zoneRepository).BuildServiceProvider();
		_resolver = Substitute.For<IDnsResolver>();
		_target = new DatabaseZoneProvider(Substitute.For<ILogger<DatabaseZoneProvider>>(), services, _resolver);
	}

	[Fact]
	public async Task Initialize_And_GetZones_MapDatabaseRecords()
	{
		_zoneRepository.GetZones().Returns(
		[
			new DbZone
			{
				Suffix = "example.com",
				Serial = 17,
				Records = [new DbZoneRecord { Host = "www", Data = "192.0.2.10", Type = ResourceType.A, Class = ResourceClass.IN }],
			},
		]
		);

		_target.Initialize(new ZoneOptions { Name = "example.com" });
		_resolver.Received(1).SubscribeTo(_target);

		var zones = await InvokePrivateAsync<List<Zone>>(_target, "GetZones");
		Assert.Single(zones);
		Assert.Equal("example.com", zones[0].Suffix);
		Assert.Equal((uint)17, zones[0].Serial);
		Assert.Equal("www", zones[0].Records[0].Host);
	}

	private static async Task<T> InvokePrivateAsync<T>(object target, string methodName)
	{
		var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
		var task = (Task<T>)method!.Invoke(target, null)!;
		return await task.ConfigureAwait(false);
	}
}
