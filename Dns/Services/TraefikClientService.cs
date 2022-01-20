using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Models.Traefik;

namespace Dns.Services
{
	public class TraefikClientService
	{
		private readonly HttpClient _client;
		private readonly AppConfig _appConfig;

		public TraefikClientService(HttpClient client, AppConfig appConfig)
		{
			_appConfig = appConfig;

			if (!_appConfig.Server.Zone.ProviderSettings.ContainsKey("traefikUrl") || !_appConfig.Server.Zone.ProviderSettings.ContainsKey("username") || !_appConfig.Server.Zone.ProviderSettings.ContainsKey("password"))
				throw new Exception("Missing settings for Traefik provider, please update your appsettings.json file");
			
			client.BaseAddress = new Uri(GetSetting("traefikUrl"));
			client.DefaultRequestHeaders.Referrer = new Uri(GetSetting("traefikUrl"));
			client.DefaultRequestHeaders.Authorization = new BasicAuthenticationHeaderValue(GetSetting("username"), GetSetting("password"));
			client.DefaultRequestHeaders.Add("Accept-language", "en");
			_client = client;
		}

		private string GetSetting(string setting) => !_appConfig.Server.Zone.ProviderSettings.ContainsKey(setting) ? throw new Exception($"Setting \"{setting}\" is missing from your appsettings.json file") : _appConfig.Server.Zone.ProviderSettings[setting];

		// ReSharper disable once MemberCanBeMadeStatic.Global
		public IPAddress GetDockerHostInternalIp() => IPAddress.Parse(GetSetting("dockerHostInternalIp"));

		public async Task<IEnumerable<Route>> GetRoutes()
		{
			using HttpResponseMessage response = await _client.GetAsync("/api/http/routers?search=&status=&per_page=1000&page=1");

			if (response.StatusCode is not HttpStatusCode.OK) throw new AuthenticationException(await response.Content.ReadAsStringAsync());
			
			return await response.Content.ReadAsAsync<IEnumerable<Route>>();
		}
	}
}