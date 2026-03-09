using Dns.Cli.Controllers;
using Dns.Services;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Dns.UnitTests;

#pragma warning disable CS0618
public sealed class DumpControllerTests
{
	private readonly IDnsService _dnsService;
	private readonly IDnsServer _dnsServer;
	private readonly DumpController _target;
	public DumpControllerTests()
	{
		_dnsService = Substitute.For<IDnsService>();
		_dnsServer = Substitute.For<IDnsServer>();
		_target = new DumpController(_dnsService, _dnsServer);
	}

	[Fact]
	public void GetDnsResolverData_ReturnsHtmlContent()
	{
		_dnsService.Resolvers.Returns([]);
		var result = _target.GetDnsResolverData();
		var content = Assert.IsType<ContentResult>(result);
		Assert.Equal("text/html", content.ContentType);
	}

	[Fact]
	public void GetDnsServerData_ReturnsHtmlContent()
	{
		var result = _target.GetDnsServerData();
		var content = Assert.IsType<ContentResult>(result);
		Assert.Equal("text/html", content.ContentType);
	}
}
#pragma warning restore CS0618