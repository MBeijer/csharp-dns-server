// // //------------------------------------------------------------------------------------------------- 
// // // <copyright file="Program.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using Dns.Config;
using Microsoft.Extensions.DependencyInjection;

namespace Dns
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using Dns.ZoneProvider.AP;

    using Ninject;
    using Microsoft.Extensions.Configuration;

    public class Program
    {
        private static IServiceProvider _services; 
        public Program(IServiceProvider services)
        {
            _services = services;
        }
        
        private static readonly IKernel Container = new StandardKernel();

        private static ZoneProvider.BaseZoneProvider _zoneProvider; // reloads Zones from machineinfo.csv changes
        private static SmartZoneResolver _zoneResolver; // resolver and delegated lookup for unsupported zones;
        private static DnsServer _dnsServer; // resolver and delegated lookup for unsupported zones;
        private static HttpServer _httpServer;
        public bool Running { get; set; } = true;

        /// <summary>
        /// DNS Server entrypoint
        /// </summary>
        /// <param name="configFile">Fully qualified configuration filename</param>
        /// <param name="cts">Cancellation Token Source</param>
        public void Run(string configFile, CancellationToken ct)
        {

            if (!File.Exists(configFile))
            {
                throw new FileNotFoundException(null, configFile);
            }

            var appConfig = _services.GetService<AppConfig>();
            var configuration = _services.GetService<IConfiguration>();
            
            Container.Bind<ZoneProvider.BaseZoneProvider>().To(ByName(appConfig.Server.Zone.Provider));
            var zoneProviderConfig = configuration.GetSection("zoneprovider");
            _zoneProvider = Container.Get<ZoneProvider.BaseZoneProvider>();
            _zoneProvider.Initialize(_services, zoneProviderConfig, appConfig.Server.Zone.Name);

            _zoneResolver = new SmartZoneResolver();
            _zoneResolver.SubscribeTo(_zoneProvider);

            _dnsServer = new DnsServer(appConfig.Server.DnsListener.Port);

            _httpServer = new HttpServer();

            _dnsServer.Initialize(_zoneResolver);

            _zoneProvider.Start(ct);
            _dnsServer.Start(ct);

            /*
            if(appConfig.Server.WebServer.Enabled)
            {
                _httpServer.Initialize($"http://+:{appConfig.Server.WebServer.Port}/");
                _httpServer.OnProcessRequest += _httpServer_OnProcessRequest;
                _httpServer.OnHealthProbe += _httpServer_OnHealthProbe;
                _httpServer.Start(ct);
            }
            */

            ct.WaitHandle.WaitOne();

        }

        private static void _httpServer_OnHealthProbe(HttpListenerContext context)
        {
        }

        private static void _httpServer_OnProcessRequest(HttpListenerContext context)
        {
            var rawUrl = context.Request.RawUrl;
            switch (rawUrl)
            {
                case "/dump/dnsresolver":
                {
                    context.Response.Headers.Add("Content-Type","text/html");
                    using var writer = context.Response.OutputStream.CreateWriter();
                    _zoneResolver.DumpHtml(writer);

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
                    _dnsServer.DumpHtml(writer);
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
}