using Dns.Db.Configuration;
using Dns.Db.Contexts;
using Dns.Db.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dns.Db.Extensions;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddDatabaseDependencies(
		this IServiceCollection services,
		DatabaseSettings databaseSettings
	)
	{
		services.AddDbContext<DnsServerDbContext>(options => options.UseSqlite(
			                                          databaseSettings.SQLiteDefault,
			                                          b => b.MigrationsAssembly("Dns.Cli")
		                                          )
		);

		services.AddScoped<IUserRepository, UserRepository>();
		services.AddScoped<IZoneRepository, ZoneRepository>();

		return services;
	}
}