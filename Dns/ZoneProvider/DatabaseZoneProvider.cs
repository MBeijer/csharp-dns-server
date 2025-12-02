using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;
using Dns.Db.Repositories;
using Dns.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static System.Threading.Tasks.Task;

namespace Dns.ZoneProvider;

/// <summary>
///     IPProbeZoneProvider map via configuration a set of monitored IPs to host A records.
///     Various monitoring strategies are implemented to detect IP health.
///     Health IP addresses are added to the Zone.
/// </summary>
public class DatabaseZoneProvider(
	ILogger<DatabaseZoneProvider> logger,
	IServiceProvider services,
	IDnsResolver dnsResolver
) : BaseZoneProvider(dnsResolver)
{
	private CancellationToken Ct          { get; set; }
	private Task              RunningTask { get; set; }

	/// <summary>Initialize ZoneProvider</summary>
	/// <param name="zoneOptions">ZoneProvider Configuration Section</param>
	public override void Initialize(ZoneOptions zoneOptions)
	{
		Zone.Suffix = zoneOptions.Name;

		base.Initialize(zoneOptions);
	}

	private void ProbeLoop(CancellationToken ct)
	{
		logger.LogInformation("Probe loop started");

		ParallelOptions options = new() { CancellationToken = ct, MaxDegreeOfParallelism = 4 };

		while (!ct.IsCancellationRequested)
		{
			var batchStartTime = DateTime.UtcNow;

			Run(GetZones, ct).ContinueWith(t => Notify(t.Result), ct);

			var batchDuration = DateTime.UtcNow - batchStartTime;
			logger.LogInformation("Probe batch duration {BatchDuration}", batchDuration);

			ct.WaitHandle.WaitOne(10 * 1000);
		}
	}

	public override void Dispose()
	{
		// cleanup
	}

	public override void Start(CancellationToken ct)
	{
		ct.Register(Stop);
		RunningTask = Run(() => ProbeLoop(ct), ct);
	}

	private void Stop() => RunningTask.Wait(Ct);

	private static List<ZoneRecord> GetZoneRecords(ICollection<Db.Models.EntityFramework.ZoneRecord> zoneRecords)
	{
		return zoneRecords.Select(z => new ZoneRecord
			                  {
				                  Host      = z.Host,
				                  Addresses = [z.Data],
				                  Count     = 1,
				                  Type      = z.Type!.Value,
				                  Class     = z.Class!.Value,
			                  }
		                  )
		                  .ToList();
	}

	private async Task<List<Zone>> GetZones()
	{
		using var scope          = services.CreateScope();
		var       zoneRepository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

		var dbZones = await zoneRepository.GetZones().ConfigureAwait(false);
		var zones = dbZones.Select(s =>
			                   {
				                   var zone = new Zone { Suffix = s.Suffix, Serial = s.Serial };

				                   zone.Initialize(GetZoneRecords(s.Records));

				                   return zone;
			                   }
		                   )
		                   .ToList();

		return zones;
	}
}