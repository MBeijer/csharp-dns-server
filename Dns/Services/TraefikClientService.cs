using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
using Dns.Config;
using Dns.ZoneProvider.Traefik;
using Dns.ZoneProvider.Traefik.Models;

namespace Dns.Services;

public class TraefikClientService(HttpClient client)
{
	private TraefikZoneProviderSettings _providerSettings;

	// ReSharper disable once MemberCanBeMadeStatic.Global
	public IPAddress GetDockerHostInternalIp() => IPAddress.Parse(_providerSettings.DockerHostInternalIp);

	public async Task<IEnumerable<Route>> GetRoutes()
	{
		using var response = await client.GetAsync("/api/http/routers?search=&status=&per_page=1000&page=1")
		                                 .ConfigureAwait(false);

		if (response.StatusCode is not HttpStatusCode.OK)
			throw new AuthenticationException(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

		var str    = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
		var routes = await response.Content.ReadAsAsync<IEnumerable<Route>>().ConfigureAwait(false);

		return routes;
	}

	public void Initialize(ZoneOptions zoneOptions)
	{
		if (zoneOptions.ProviderSettings is not TraefikZoneProviderSettings providerSettings)
			throw new("Missing settings for Traefik provider, please update your appsettings.json file");
		_providerSettings = providerSettings;

		client.BaseAddress                    = new(_providerSettings.TraefikUrl);
		client.DefaultRequestHeaders.Referrer = new(_providerSettings.TraefikUrl);
		client.DefaultRequestHeaders.Authorization = new BasicAuthenticationHeaderValue(
			_providerSettings.Username,
			_providerSettings.Password
		);
		client.DefaultRequestHeaders.Add("Accept-language", "en");
	}
}