using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Dns.Cli;

/// <summary>
///
/// </summary>
public static class Program
{
	/// <summary>
	///
	/// </summary>
	/// <param name="args"></param>
	public static Task Main(string[] args)
		=> CreateWebHostBuilder(args).Build().RunAsync();

	private static IHostBuilder CreateWebHostBuilder(string[] args) =>
		Host.CreateDefaultBuilder(args)
		    .ConfigureAppConfiguration(
			    (context, config) =>
			    {
				    config.AddEnvironmentVariables();
				    config.AddCommandLine(args);
				    var tempConfig = config.Build();
				    var customPath = tempConfig["appsettings"];

				    if (!string.IsNullOrWhiteSpace(customPath))
				    {
					    if (File.Exists(customPath))
					    {
						    config.AddJsonFile(customPath, optional: false, reloadOnChange: true);
					    }
				    }
			    }
		    )
		    .ConfigureWebHostDefaults(webHost => webHost.UseStartup<Startup>());
}