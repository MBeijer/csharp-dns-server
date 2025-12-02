using Dns.Db.Contexts;
using Dns.Db.Models.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dns.Db.Repositories;

#pragma warning disable CS9113

public class ZoneRepository(ILogger<ZoneRepository> logger, DnsServerDbContext dbContext) : IZoneRepository
{
	public Task<List<Zone>> GetZones() => dbContext.Zones!.AsNoTracking().ToListAsync();

	public Task<Zone?> GetZone(string suffix) =>
		dbContext.Zones!.Where(x => x.Suffix! == suffix).SingleOrDefaultAsync();

	public async Task AddZone(Zone zone)
	{
		await dbContext.Zones!.AddAsync(zone).ConfigureAwait(false);
		await dbContext.SaveChangesAsync().ConfigureAwait(false);
	}
}

#pragma warning restore CS9113