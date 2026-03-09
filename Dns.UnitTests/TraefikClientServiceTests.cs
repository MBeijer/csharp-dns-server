using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Services;
using Dns.ZoneProvider.Traefik;
using Xunit;

namespace Dns.UnitTests;

public sealed class TraefikClientServiceTests
{
	private readonly StubHttpMessageHandler _successHandler;
	private readonly HttpClient _client;
	private readonly TraefikClientService _target;

	public TraefikClientServiceTests()
	{
		_successHandler = new(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(
				"[{\"name\":\"route-1\",\"rule\":\"Host(`example.com`)\",\"status\":\"enabled\"}]",
				Encoding.UTF8,
				"application/json"
			),
		});
		_client = new(_successHandler);
		_target = new TraefikClientService(_client);
	}

	[Fact]
	public async Task Initialize_And_GetRoutes_CoversSuccessPath()
	{
		_target.Initialize(CreateSettings());
		Assert.Equal("http://localhost:8080/", _client.BaseAddress?.ToString());
		Assert.Equal("172.17.0.1", _target.GetDockerHostInternalIp().ToString());
		var routes = (await _target.GetRoutes()).ToList();
		Assert.Single(routes);
		Assert.Equal("route-1", routes[0].Name);
	}

	[Fact]
	public async Task GetRoutes_Throws_WhenUnauthorized()
	{
		var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("denied") });
		var target = new TraefikClientService(new HttpClient(handler));
		target.Initialize(CreateSettings());
		await Assert.ThrowsAsync<AuthenticationException>(() => target.GetRoutes());
	}

	[Fact]
	public void Initialize_Throws_WhenProviderSettingsMissing()
	{
		Assert.Throws<Exception>(() => _target.Initialize(new ZoneOptions()));
	}

	private static ZoneOptions CreateSettings() =>
		new()
		{
			ProviderSettings = new TraefikZoneProviderSettings
			{
				TraefikUrl = "http://localhost:8080",
				Username = "u",
				Password = "p",
				DockerHostInternalIp = "172.17.0.1",
			}
		};

	private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(response);
	}
}