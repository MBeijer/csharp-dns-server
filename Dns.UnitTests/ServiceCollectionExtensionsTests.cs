using Dns.Db.Configuration;
using Dns.Db.Contexts;
using Dns.Db.Extensions;
using Dns.Db.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dns.UnitTests;

public sealed class ServiceCollectionExtensionsTests
{
	private readonly ServiceCollection _services;

	public ServiceCollectionExtensionsTests() => _services = [];

	[Fact]
	public void AddDatabaseDependencies_RegistersExpectedServices()
	{
		_services.AddDatabaseDependencies(new DatabaseSettings { SQLiteDefault = "Data Source=:memory:" });
		Assert.Contains(_services, s => s.ServiceType == typeof(DnsServerDbContext));
		Assert.Contains(_services, s => s.ServiceType == typeof(IUserRepository) && s.ImplementationType == typeof(UserRepository));
		Assert.Contains(_services, s => s.ServiceType == typeof(IZoneRepository) && s.ImplementationType == typeof(ZoneRepository));
	}
}