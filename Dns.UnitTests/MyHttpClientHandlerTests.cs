using System.Net;
using System.Net.Http;
using Dns.Handlers;
using Xunit;

namespace Dns.UnitTests;

public sealed class MyHttpClientHandlerTests
{
	private readonly CookieContainer _cookieContainer;
	private readonly TestHttpClientHandler _target;

	public MyHttpClientHandlerTests()
	{
		_cookieContainer = new CookieContainer();
		_target = new TestHttpClientHandler(_cookieContainer);
	}

	[Fact]
	public void Constructor_SetsExpectedDefaults()
	{
		Assert.Equal(ClientCertificateOption.Manual, _target.ClientCertificateOptions);
		Assert.False(_target.AllowAutoRedirect);
		Assert.Same(_cookieContainer, _target.CookieContainer);
		Assert.True(_target.ServerCertificateCustomValidationCallback?.Invoke(null, null, null, default) ?? false);
	}

	private sealed class TestHttpClientHandler(CookieContainer cookieContainer) : MyHttpClientHandler(cookieContainer);
}
