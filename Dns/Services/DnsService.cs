// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="Program.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;
using Dns.ZoneProvider;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dns.Services;

public class DnsService(IServiceProvider services, IOptions<ServerOptions> serverOptions, IDnsServer dnsServer)
	: IDnsService
{
	private static readonly List<IDnsResolver>      ZoneResolvers = [];
	public                  bool                    Running { get; set; } = true;
	private                 CancellationTokenSource Cts     { get; set; }

	public List<IDnsResolver> Resolvers => ZoneResolvers;

	public async Task StartAsync(CancellationToken ct)
	{
		Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

		foreach (var zone in serverOptions.Value.Zones)
		{
			var zoneProvider = (IZoneProvider)services.GetRequiredService(ByName(zone.Provider));
			zoneProvider.Initialize(zone);
			zoneProvider.Start(Cts.Token);
			ZoneResolvers.Add(zoneProvider.Resolver);
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

	private static Type ByName(string name) =>
		AppDomain.CurrentDomain.GetAssemblies()
		         .Reverse()
		         .Select(assembly => assembly.GetType(name))
		         .FirstOrDefault(tt => tt != null);
}