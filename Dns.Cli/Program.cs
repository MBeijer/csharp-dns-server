using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Dns.Cli;

/// <summary>
/// </summary>
public static class Program
{
	/// <summary>
	///     Code borrowed from https://stackoverflow.com/questions/1600962/displaying-the-build-date
	/// </summary>
	public static DateTime BuildDateTime
	{
		get
		{
			var attribute = Assembly.GetExecutingAssembly()
									.GetCustomAttributes<AssemblyMetadataAttribute>()
									.FirstOrDefault(a => a.Key == "BuildTime");

			return attribute != null && DateTime.TryParse(attribute.Value, out var date) ? date : default;
		}
	}

	/// <summary>
	///
	/// </summary>
	public static string? BuildVersion
	{
		get
		{
			var attribute = Assembly.GetExecutingAssembly()
									.GetCustomAttributes<AssemblyMetadataAttribute>()
									.FirstOrDefault(a => a.Key == "BuildVersion");

			return attribute?.Value;
		}
	}

	/// <summary>
	/// </summary>
	/// <param name="args"></param>
	public static Task Main(string[] args)
		=> CreateWebHostBuilder(args).Build().RunAsync();

	private static IHostBuilder CreateWebHostBuilder(string[] args) =>
		Host.CreateDefaultBuilder(args)
			.ConfigureAppConfiguration(
				(_, config) =>
				{
					config.AddEnvironmentVariables();
					config.AddCommandLine(args);
					var tempConfig = config.Build();
					var customPath = tempConfig["appsettings"];

					if (!string.IsNullOrWhiteSpace(customPath))
						if (File.Exists(customPath))
							config.AddJsonFile(customPath, false, true);
				}
			)
			.ConfigureWebHostDefaults(webHost => webHost.UseStartup<Startup>());
}