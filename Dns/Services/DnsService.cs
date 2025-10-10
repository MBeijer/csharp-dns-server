// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="Program.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;
using Dns.Extensions;
using Dns.ZoneProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Dns.Services;

public class DnsService(IServiceProvider services, AppConfig appConfig, IDnsServer dnsServer) : IDnsService
{
    private static readonly List<IDnsResolver> ZoneResolvers = []; // reloads Zones from machineinfo.csv changes
    public                  bool               Running { get; set; } = true;

    public  List<IDnsResolver> Resolvers => ZoneResolvers;
    public async Task StartAsync(CancellationToken ct)
    {
        foreach (var zone in appConfig.Server.Zones)
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

    private void _httpServer_OnProcessRequest(HttpListenerContext context)
    {
        var rawUrl = context.Request.RawUrl;
        switch (rawUrl)
        {
            case "/dump/dnsresolver":
            {
                context.Response.Headers.Add("Content-Type","text/html");
                using var writer = context.Response.OutputStream.CreateWriter();
                foreach (var zoneResolver in ZoneResolvers)
                    zoneResolver.DumpHtml(writer);

                break;
            }
            case "/dump/dnsserver":
            {
                context.Response.Headers.Add("Content-Type", "text/html");
                using var writer = context.Response.OutputStream.CreateWriter();
                dnsServer.DumpHtml(writer);
                break;
            }
        }
    }

    private static Type ByName(string name) => AppDomain.CurrentDomain.GetAssemblies().Reverse().Select(assembly => assembly.GetType(name)).FirstOrDefault(tt => tt != null);
}