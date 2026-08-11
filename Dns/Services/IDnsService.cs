using System.Collections.Generic;
using System.Threading.Tasks;
using Dns.Contracts;
using Dns.Models;
using Microsoft.Extensions.Hosting;

namespace Dns.Services;

public interface IDnsService : IHostedService
{
	public List<IDnsResolver>                Resolvers   { get; }
	public IReadOnlyList<ActiveZoneSnapshot> ActiveZones { get; }

	Task<BindZoneImportBatchResult> ImportActiveBindZonesToDatabaseAndDisableAsync(
		bool replaceExistingRecords = true,
		bool enableImportedZones = true
	);
}

public sealed record ActiveZoneSnapshot(Zone Zone, string Source, bool IsReplicated);