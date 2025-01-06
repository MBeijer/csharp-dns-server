using System;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Handlers;
using Dns.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace DnsCli;

/// <summary>Stub program that enables DNS Server to run from the command line</summary>
internal static class Program
{
    private static readonly CancellationTokenSource Cts         = new();
    private static readonly ManualResetEvent        ExitTimeout = new(false);

    public static async Task Main(string[] args)
    {
        Console.CancelKeyPress += Console_CancelKeyPress;

        Console.WriteLine("DNS Server - Console Mode");

        if(args.Length == 0) args = ["./appsettings.json"];

        var builder = Host.CreateDefaultBuilder().ConfigureServices((hostContext, services) =>
        {
            services.AddAutoMapper(typeof(Program).Assembly);
            services.AddSingleton(services);
            services.AddSingleton<Dns.Program>();

            //string homePath = Environment.OSVersion.Platform is PlatformID.Unix or PlatformID.MacOSX ? Environment.GetEnvironmentVariable("HOME") + "/.config/tbnotify" : Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            //_settings = Settings.CreateConfig($"{homePath}{Path.DirectorySeparatorChar}{arguments.ConfigurationFile}").Result;

            //services.AddSingleton<Settings>(_settings);
            Console.WriteLine("Read config:");
            IConfiguration configuration = new ConfigurationBuilder()
                                           .AddJsonFile(args[0], true, true)
                                           .Build();
            Console.WriteLine("Done!");

            var appConfig = configuration.Get<AppConfig>();

            services.AddSingleton(configuration);
            services.AddSingleton(appConfig);
            services.AddTransient<TraefikClientHandler>();
            services.AddHttpClient<TraefikClientService>().ConfigurePrimaryHttpMessageHandler<TraefikClientHandler>();

            services.AddLogging(
                configure =>
                {
                    configure.AddSimpleConsole(options =>
                    {
                        options.IncludeScopes = true;
                        options.SingleLine = true;
                        options.TimestampFormat = "[hh:mm:ss] ";
                        options.ColorBehavior = LoggerColorBehavior.Enabled;
                    });

                    // if (_settings?.General?.Loglevel == Debug)
                    //     configure.SetMinimumLevel(LogLevel.Debug);
                }
            );
            services.RemoveAll<IHttpMessageHandlerBuilderFilter>();
        }).UseConsoleLifetime();

        builder.ConfigureHostConfiguration(config =>
        {
            if (args != null)
                // environment from command line
                // e.g.: dotnet run --environment "Staging"
                config.AddCommandLine(args);
        }).ConfigureAppConfiguration((context, builder) => { builder.SetBasePath(AppContext.BaseDirectory).AddEnvironmentVariables(); });


        var host = builder.Build();

        using var serviceScope = host.Services.CreateScope();
        {
            var services = serviceScope.ServiceProvider;

            try
            {
                var myService = services.GetRequiredService<Dns.Program>();

                //myService?.Init(Cts.Token);

                while (myService is { Running: true })
                {

                    /*await*/ myService?.Run(args[0], Cts.Token);
                    //Thread.Sleep(Engine.DefaultTicks*1000);
                }

                await Task.CompletedTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Occured: {ex}");
            }
        }



        ExitTimeout.Set();

    }

    private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
    {
        Console.WriteLine("\r\nShutting Down");
        Cts.Cancel();
        ExitTimeout.WaitOne(5000);
    }
}