// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="Program.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;
using Dns.Db.Repositories;
using Dns.ZoneProvider;
using Dns.ZoneProvider.Bind;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dns.Services;

public class DnsService(IServiceProvider services, IOptions<ServerOptions> serverOptions, IDnsServer dnsServer)
	: IDnsService
{
	private sealed class ProviderRuntime(ZoneOptions zoneOptions, IZoneProvider provider)
	{
		public ZoneOptions  ZoneOptions { get; } = zoneOptions;
		public IZoneProvider Provider    { get; } = provider;
	}

	private static readonly Lock                    RuntimeSyncRoot = new();
	private static readonly List<IDnsResolver>      ZoneResolvers = [];
	private static readonly List<ProviderRuntime>   ActiveProviders = [];
	public                  bool                    Running { get; set; } = true;
	private                 CancellationTokenSource Cts     { get; set; }

	public List<IDnsResolver> Resolvers => ZoneResolvers;

	public async Task StartAsync(CancellationToken ct)
	{
		Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

		lock (RuntimeSyncRoot)
		{
			ZoneResolvers.Clear();
			ActiveProviders.Clear();
		}

		foreach (var zone in serverOptions.Value.Zones)
		{
			var zoneProvider = (IZoneProvider)services.GetRequiredService(ByName(zone.Provider));
			zoneProvider.Initialize(zone);
			zoneProvider.Start(Cts.Token);
			lock (RuntimeSyncRoot)
			{
				ZoneResolvers.Add(zoneProvider.Resolver);
				ActiveProviders.Add(new(zone, zoneProvider));
			}
		}

		dnsServer.Initialize(ZoneResolvers);
		await dnsServer.Start(Cts.Token).ConfigureAwait(false);
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		if (Cts != null)
			await Cts.CancelAsync().ConfigureAwait(false);

		// Wait until the task completes or the stop timeout occurs
		await Task.WhenAny(Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
	}

	public async Task<BindZoneImportBatchResult> ImportActiveBindZonesToDatabaseAndDisableAsync(
		bool replaceExistingRecords = true,
		bool enableImportedZones = true
	)
	{
		List<ProviderRuntime> activeBindProviders;
		lock (RuntimeSyncRoot)
		{
			activeBindProviders = ActiveProviders
			                      .Where(runtime => runtime.Provider is BindZoneProvider)
			                      .ToList();
		}

		var result = new BindZoneImportBatchResult();

		using var scope          = services.CreateScope();
		var       zoneRepository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

		foreach (var runtime in activeBindProviders)
		{
			var item = new BindZoneImportBatchItem
			{
				ZoneSuffix = runtime.ZoneOptions.Name ?? string.Empty,
				FileName   = (runtime.ZoneOptions.ProviderSettings as FileWatcherZoneProviderSettings)?.FileName ?? string.Empty,
			};

			try
			{
				if (runtime.ZoneOptions.ProviderSettings is not FileWatcherZoneProviderSettings fileSettings ||
				    string.IsNullOrWhiteSpace(fileSettings.FileName))
					throw new InvalidOperationException("BIND provider is missing file watcher settings.");

				var bindFilePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(fileSettings.FileName));
				var parsedZone   = BindZoneProvider.ParseZoneFile(bindFilePath, runtime.ZoneOptions.Name);
				var dbZone     = BindZoneImportMapper.ToDbZone(parsedZone, runtime.ZoneOptions.Name, enableImportedZones);
				var upserted   = await zoneRepository.UpsertZone(dbZone, replaceExistingRecords).ConfigureAwait(false);

				item.Imported   = true;
				item.ZoneId     = upserted.Id;
				item.Serial     = upserted.Serial;
				item.RecordCount = upserted.Records?.Count ?? dbZone.Records?.Count ?? 0;
				result.ImportedCount++;

				((IObserver<List<Dns.Models.Zone>>)runtime.Provider.Resolver).OnNext([]);
				if (runtime.Provider is IDisposable disposableProvider)
					disposableProvider.Dispose();
				item.Disabled = true;
				result.DisabledCount++;

				lock (RuntimeSyncRoot)
				{
					ActiveProviders.Remove(runtime);
				}
			}
			catch (Exception ex)
			{
				item.Error = ex.Message;
				result.FailedCount++;
			}

			result.Items.Add(item);
		}

		return result;
	}

	private static Type ByName(string name) =>
		AppDomain.CurrentDomain.GetAssemblies()
		         .Reverse()
		         .Select(assembly => assembly.GetType(name))
		         .FirstOrDefault(tt => tt != null);
}
