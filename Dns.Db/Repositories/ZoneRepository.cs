using System;
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
	public async Task UpdateZone(Zone zone)
	{
		dbContext.Zones!.Update(zone);
		await dbContext.SaveChangesAsync().ConfigureAwait(false);
	}

	public async Task<Zone> UpsertZone(Zone zone, bool replaceRecords = true)
	{
		ArgumentNullException.ThrowIfNull(zone);
		if (string.IsNullOrWhiteSpace(zone.Suffix))
			throw new ArgumentException("Zone suffix is required.", nameof(zone));

		var existing = await dbContext.Zones!
									  .Include(z => z.Records)
									  .SingleOrDefaultAsync(z => z.Suffix == zone.Suffix)
									  .ConfigureAwait(false);

		if (existing == null)
		{
			await dbContext.Zones!.AddAsync(zone).ConfigureAwait(false);
			await dbContext.SaveChangesAsync().ConfigureAwait(false);
			return zone;
		}

		existing.Serial = zone.Serial;
		existing.Enabled = zone.Enabled;

		if (replaceRecords)
		{
			if (existing.Records?.Count > 0) dbContext.ZoneRecords!.RemoveRange(existing.Records);

			existing.Records = zone.Records ?? [];
			foreach (var record in existing.Records)
			{
				record.Id = null;
				record.ZoneObj = existing;
				record.Zone = existing.Id;
			}
		}

		await dbContext.SaveChangesAsync().ConfigureAwait(false);
		return existing;
	}
}

#pragma warning restore CS9113