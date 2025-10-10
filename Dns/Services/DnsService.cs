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

public class DnsService(IServiceProvider services, IOptions<ServerOptions> serverOptions, IDnsServer dnsServer) : IDnsService
{
    private static readonly List<IDnsResolver> ZoneResolvers = [];
    public                  bool               Running { get; set; } = true;

    public  List<IDnsResolver> Resolvers => ZoneResolvers;
    public async Task StartAsync(CancellationToken ct)
    {
        foreach (var zone in serverOptions.Value.Zones)
        {
            var zoneProvider = (IZoneProvider)services.GetRequiredService(ByName(zone.Provider));
            zoneProvider.Initialize(zone);
            zoneProvider.Start(ct);
            ZoneResolvers.Add(zoneProvider.Resolver);
        }

        dnsServer.Initialize(ZoneResolvers);
        await dnsServer.Start(ct).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static Type ByName(string name) => AppDomain.CurrentDomain.GetAssemblies().Reverse().Select(assembly => assembly.GetType(name)).FirstOrDefault(tt => tt != null);
}