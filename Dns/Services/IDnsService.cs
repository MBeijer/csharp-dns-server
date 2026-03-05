using System.Collections.Generic;
using System.Threading.Tasks;
using Dns.Contracts;
using Microsoft.Extensions.Hosting;

namespace Dns.Services;

public interface IDnsService : IHostedService
{
	public List<IDnsResolver> Resolvers { get; }

	Task<BindZoneImportBatchResult> ImportActiveBindZonesToDatabaseAndDisableAsync(
		bool replaceExistingRecords = true,
		bool enableImportedZones = true
	);
}
