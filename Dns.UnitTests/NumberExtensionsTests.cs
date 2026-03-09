using Dns.Extensions;
using Xunit;

namespace Dns.UnitTests;

public sealed class NumberExtensionsTests
{
	[Fact]
	public void Ip_ReturnsStringAddress()
	{
		var value = 0x0100007FL;
		var ip = value.IP();
		Assert.Equal("127.0.0.1", ip);
	}
}