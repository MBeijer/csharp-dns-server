using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Dns.Cli;

public static class Program
{
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