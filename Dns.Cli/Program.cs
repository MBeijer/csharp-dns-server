using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

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
	public static async Task Main(string[] args) => await CreateWebHostBuilder(args).Build().RunAsync().ConfigureAwait(false);

	private static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
		WebHost.CreateDefaultBuilder(args)
		       .ConfigureAppConfiguration(
			       (hostingContext, config) =>
			       {
				       config.AddEnvironmentVariables();
				       config.AddCommandLine(args);
			       }
		       )
		       .UseStartup<Startup>();
}