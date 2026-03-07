using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Dns.Db.Contexts;
using Dns.Db.Models.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dns.Db.Repositories;

#pragma warning disable CS9113

public class ZoneRepository(ILogger<ZoneRepository> logger, DnsServerDbContext dbContext) : IZoneRepository
{
	public Task<List<Zone>> GetZones() =>
		dbContext.Zones!
				 .AsNoTracking()
				 .Include(z => z.MasterZone)
				 .Include(z => z.SlaveZones)
				 .ToListAsync();

	public Task<Zone?> GetZone(string suffix) =>
		dbContext.Zones!
				 .Include(z => z.MasterZone)
				 .Include(z => z.SlaveZones)
				 .Where(x => x.Suffix! == suffix)
				 .SingleOrDefaultAsync();

	public async Task AddZone(Zone zone)
	{
		ArgumentNullException.ThrowIfNull(zone);
		await ValidateMasterReferenceAsync(zone.MasterZoneId, null).ConfigureAwait(false);
		zone.Serial = GetNextSerial(null);
		NormalizeSoaSerial(zone.Records, zone.Serial);

		await dbContext.Zones!.AddAsync(zone).ConfigureAwait(false);
		await dbContext.SaveChangesAsync().ConfigureAwait(false);

		if (zone.MasterZoneId != null)
		{
			var inserted = await dbContext.Zones!
										 .Include(z => z.Records)
										 .SingleAsync(z => z.Id == zone.Id)
										 .ConfigureAwait(false);
			await SynchronizeFromMasterAsync(inserted, zone.MasterZoneId.Value).ConfigureAwait(false);
			await dbContext.SaveChangesAsync().ConfigureAwait(false);
		}
	}

	public async Task UpdateZone(Zone zone)
	{
		ArgumentNullException.ThrowIfNull(zone);
		if (zone.Id == null) throw new ArgumentException("Zone id is required.", nameof(zone));

		var existing = await dbContext.Zones!
									  .Include(z => z.Records)
									  .SingleOrDefaultAsync(z => z.Id == zone.Id)
									  .ConfigureAwait(false);

		if (existing == null) throw new InvalidOperationException($"Zone '{zone.Id}' was not found.");
		await ValidateMasterReferenceAsync(zone.MasterZoneId, existing.Id).ConfigureAwait(false);

		if (existing.MasterZoneId != null && zone.MasterZoneId == existing.MasterZoneId)
			throw new InvalidOperationException("Slave zones cannot be edited directly while connected to a master zone.");

		existing.Suffix = zone.Suffix;
		existing.MasterZoneId = zone.MasterZoneId;

		if (existing.MasterZoneId != null)
		{
			await SynchronizeFromMasterAsync(existing, existing.MasterZoneId.Value).ConfigureAwait(false);
		}
		else
		{
			existing.Serial = GetNextSerial(existing.Serial);
			existing.Enabled = zone.Enabled;
			NormalizeSoaSerial(zone.Records, existing.Serial);

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

		if (existing.MasterZoneId == null && existing.Id != null)
		{
			await SynchronizeSlavesAsync(existing.Id.Value).ConfigureAwait(false);
			await dbContext.SaveChangesAsync().ConfigureAwait(false);
		}
	}

	public async Task<bool> DeleteZone(int id)
	{
		var existing = await dbContext.Zones!
									  .Include(z => z.Records)
									  .SingleOrDefaultAsync(z => z.Id == id)
									  .ConfigureAwait(false);
		if (existing == null) return false;

		if (existing.Records?.Count > 0) dbContext.ZoneRecords!.RemoveRange(existing.Records);
		dbContext.Zones!.Remove(existing);
		await dbContext.SaveChangesAsync().ConfigureAwait(false);
		return true;
	}

	public async Task<Zone> UpsertZone(Zone zone, bool replaceRecords = true)
	{
		ArgumentNullException.ThrowIfNull(zone);
		if (string.IsNullOrWhiteSpace(zone.Suffix))
			throw new ArgumentException("Zone suffix is required.", nameof(zone));
		await ValidateMasterReferenceAsync(zone.MasterZoneId, zone.Id).ConfigureAwait(false);

		var existing = await dbContext.Zones!
									  .Include(z => z.Records)
									  .SingleOrDefaultAsync(z => z.Suffix == zone.Suffix)
									  .ConfigureAwait(false);

		if (existing == null)
		{
			zone.Serial = GetNextSerial(null);
			NormalizeSoaSerial(zone.Records, zone.Serial);
			await dbContext.Zones!.AddAsync(zone).ConfigureAwait(false);
			await dbContext.SaveChangesAsync().ConfigureAwait(false);

			if (zone.MasterZoneId != null)
			{
				await SynchronizeFromMasterAsync(zone, zone.MasterZoneId.Value).ConfigureAwait(false);
				await dbContext.SaveChangesAsync().ConfigureAwait(false);
			}

			return zone;
		}
		if (existing.MasterZoneId != null)
			throw new InvalidOperationException("Slave zones cannot be updated directly while connected to a master zone.");

		existing.Serial = GetNextSerial(existing.Serial);
		existing.Enabled = zone.Enabled;
		existing.MasterZoneId = zone.MasterZoneId;
		NormalizeSoaSerial(zone.Records, existing.Serial);

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
		if (existing.MasterZoneId == null && existing.Id != null)
		{
			await SynchronizeSlavesAsync(existing.Id.Value).ConfigureAwait(false);
			await dbContext.SaveChangesAsync().ConfigureAwait(false);
		}

		return existing;
	}

	private async Task ValidateMasterReferenceAsync(int? masterZoneId, int? zoneId)
	{
		if (masterZoneId == null) return;
		if (zoneId != null && masterZoneId == zoneId)
			throw new InvalidOperationException("A zone cannot be the master of itself.");

		var masterExists = await dbContext.Zones!.AnyAsync(z => z.Id == masterZoneId).ConfigureAwait(false);
		if (!masterExists) throw new InvalidOperationException($"Master zone '{masterZoneId}' was not found.");
	}

	private async Task SynchronizeSlavesAsync(int masterZoneId)
	{
		var master = await dbContext.Zones!
									.Include(z => z.Records)
									.SingleOrDefaultAsync(z => z.Id == masterZoneId)
									.ConfigureAwait(false);
		if (master == null) return;

		var slaves = await dbContext.Zones!
									.Include(z => z.Records)
									.Where(z => z.MasterZoneId == masterZoneId)
									.ToListAsync()
									.ConfigureAwait(false);

		foreach (var slave in slaves)
		{
			CopyMasterToSlave(master, slave);
		}
	}

	private async Task SynchronizeFromMasterAsync(Zone slave, int masterZoneId)
	{
		var master = await dbContext.Zones!
									.Include(z => z.Records)
									.SingleAsync(z => z.Id == masterZoneId)
									.ConfigureAwait(false);

		CopyMasterToSlave(master, slave);
	}

	private void CopyMasterToSlave(Zone master, Zone slave)
	{
		slave.Serial = master.Serial;
		slave.Enabled = master.Enabled;

		if (slave.Records?.Count > 0) dbContext.ZoneRecords!.RemoveRange(slave.Records);

		slave.Records = master.Records?.Select(CloneRecord).ToList() ?? new List<ZoneRecord>();
		foreach (var record in slave.Records)
		{
			record.Id = null;
			record.ZoneObj = slave;
			record.Zone = slave.Id;
		}
	}

	private static ZoneRecord CloneRecord(ZoneRecord source) =>
		new()
		{
			Host = source.Host,
			Class = source.Class,
			Type = source.Type,
			Data = source.Data,
		};

	private static void NormalizeSoaSerial(ICollection<ZoneRecord>? records, uint serial)
	{
		if (records == null) return;

		foreach (var record in records)
		{
			if (!string.Equals(record.Type?.ToString(), "SOA", StringComparison.OrdinalIgnoreCase)) continue;
			if (string.IsNullOrWhiteSpace(record.Data)) continue;

			var parts = record.Data.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
			if (parts.Count >= 3)
			{
				parts[2] = serial.ToString(CultureInfo.InvariantCulture);
				record.Data = string.Join(" ", parts);
			}
		}
	}

	private static uint GetNextSerial(uint? currentSerial)
	{
		var datePrefix = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		var prefix = uint.Parse(datePrefix, CultureInfo.InvariantCulture);

		if (currentSerial != null)
		{
			var currentPrefix = currentSerial.Value / 100;
			if (currentPrefix == prefix)
			{
				var iteration = currentSerial.Value % 100;
				if (iteration >= 99)
					throw new InvalidOperationException($"Zone serial iteration exceeded 99 for {datePrefix}.");

				return currentSerial.Value + 1;
			}
		}

		return prefix * 100 + 1;
	}
}

#pragma warning restore CS9113
