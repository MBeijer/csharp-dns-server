using Dns.Db.Contexts;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Dns.Cli.Extensions;

/// <summary>
///
/// </summary>
public static class ApplicationBuilderExtensions
{
	/// <summary>
	///
	/// </summary>
	/// <param name="app"></param>
	public static void UpdateDatabase(this IApplicationBuilder app)
	{
		using var serviceScope = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
		using var context      = serviceScope.ServiceProvider.GetService<DnsServerDbContext>();

		var migrator = context?.Database.GetService<IMigrator>();
		migrator?.Migrate();
	}
}