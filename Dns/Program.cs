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
using Dns.Config;
using Dns.Contracts;
using Dns.ZoneProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Dns;

public class Program(IServiceProvider services, AppConfig appConfig, DnsServer dnsServer)
{
    private static readonly List<IDnsResolver> ZoneResolvers = []; // reloads Zones from machineinfo.csv changes
    private static          HttpServer         _httpServer;
    public                  bool               Running { get; set; } = true;

    /// <summary>
    /// DNS Server entrypoint
    /// </summary>
    /// <param name="ct">Cancellation Token Source</param>
    public void Run(CancellationToken ct)
    {
        foreach (var zone in appConfig.Server.Zones)
        {
            var zoneProvider = (IZoneProvider)services.GetRequiredService(ByName(zone.Provider));
            zoneProvider.Initialize(zone);
            
            ZoneResolvers.Add(zoneProvider.Resolver);
            
            zoneProvider.Start(ct);
        }

        dnsServer.Initialize(ZoneResolvers);

        _httpServer = new();
        
        if(appConfig.Server.WebServer.Enabled)
        {
            _httpServer.Initialize($"http://+:{appConfig.Server.WebServer.Port}/");
            _httpServer.OnProcessRequest += _httpServer_OnProcessRequest;
            _httpServer.OnHealthProbe += _httpServer_OnHealthProbe;
            _httpServer.Start(ct);
        }
        
        dnsServer.Start(ct);

        ct.WaitHandle.WaitOne();
    }

    private static void _httpServer_OnHealthProbe(HttpListenerContext context)
    {
    }

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
            case "/dump/httpserver":
            {
                context.Response.Headers.Add("Content-Type", "text/html");
                using var writer = context.Response.OutputStream.CreateWriter();
                _httpServer.DumpHtml(writer);
                break;
            }
            case "/dump/dnsserver":
            {
                context.Response.Headers.Add("Content-Type", "text/html");
                using var writer = context.Response.OutputStream.CreateWriter();
                dnsServer.DumpHtml(writer);
                break;
            }
            case "/dump/zoneprovider":
            {
                context.Response.Headers.Add("Content-Type", "text/html");
                using var writer = context.Response.OutputStream.CreateWriter();
                _httpServer.DumpHtml(writer);
                break;
            }
        }
    }

    private static Type ByName(string name) => AppDomain.CurrentDomain.GetAssemblies().Reverse().Select(assembly => assembly.GetType(name)).FirstOrDefault(tt => tt != null);

}