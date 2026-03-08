using Dns.Config;
using Xunit;

namespace Dns.UnitTests;

public sealed class AppConfigTests
{
	[Fact]
	public void Properties_CanBeInitialized()
	{
		var zoneTransfer = new ZoneTransferOptions { Enabled = true, InjectedNsAddress = "192.0.2.10" };
		var listener = new DnsListenerOptions { Port = 53, TcpPort = 5335 };
		var zone = new ZoneOptions { Name = "example", Provider = "database" };
		var web = new WebServerOptions { JwtSecretKey = "secret" };
		var server = new ServerOptions { DnsListener = listener, WebServer = web, Zones = [zone], ZoneTransfer = zoneTransfer };
		var app = new AppConfig { Server = server };

		Assert.Equal((ushort)53, app.Server.DnsListener.Port);
		Assert.Equal("database", app.Server.Zones[0].Provider);
		Assert.Equal("secret", app.Server.WebServer.JwtSecretKey);
		Assert.True(app.Server.ZoneTransfer.Enabled);
	}
}
