using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
using Dns.Config;
using Dns.ZoneProvider.Traefik.Models;

namespace Dns.Services;

public class TraefikClientService(HttpClient client)
{
	private          ZoneOptions _zoneOptions;

	private string GetSetting(string setting) => !_zoneOptions.ProviderSettings.ContainsKey(setting) ? throw new($"Setting \"{setting}\" is missing from your appsettings.json file") : _zoneOptions.ProviderSettings[setting];

	// ReSharper disable once MemberCanBeMadeStatic.Global
	public IPAddress GetDockerHostInternalIp() => IPAddress.Parse(GetSetting("dockerHostInternalIp"));

	public async Task<IEnumerable<Route>> GetRoutes()
	{
		using var response = await client.GetAsync("/api/http/routers?search=&status=&per_page=1000&page=1");

		if (response.StatusCode is not HttpStatusCode.OK) throw new AuthenticationException(await response.Content.ReadAsStringAsync());

		var str = await response.Content.ReadAsStringAsync();
		var routes = await response.Content.ReadAsAsync<IEnumerable<Route>>();

		return routes;
	}

	public void Initialize(ZoneOptions zoneOptions)
	{
		_zoneOptions = zoneOptions;
		if (!_zoneOptions.ProviderSettings.ContainsKey("traefikUrl") || !_zoneOptions.ProviderSettings.ContainsKey("username") || !_zoneOptions.ProviderSettings.ContainsKey("password"))
			throw new("Missing settings for Traefik provider, please update your appsettings.json file");

		client.BaseAddress = new(GetSetting("traefikUrl"));
		client.DefaultRequestHeaders.Referrer = new(GetSetting("traefikUrl"));
		client.DefaultRequestHeaders.Authorization = new BasicAuthenticationHeaderValue(GetSetting("username"), GetSetting("password"));
		client.DefaultRequestHeaders.Add("Accept-language", "en");
	}
}