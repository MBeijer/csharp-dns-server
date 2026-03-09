using System.Text;
using Dns.Extensions;
using Xunit;

namespace Dns.UnitTests;

public sealed class StringExtensionsTests
{
	[Fact]
	public void GetResourceBytes_ProducesDnsLabelFormat()
	{
		var bytes = "www.example".GetResourceBytes();
		Assert.Equal(3, bytes[0]);
		Assert.Equal((byte)'w', bytes[1]);
		Assert.Equal(7, bytes[4]);
		Assert.Equal(0, bytes[12]);
	}

	[Fact]
	public void GetResourceBytes_IgnoresTrailingDot()
	{
		var bytes = "ns1.example.com.".GetResourceBytes();
		Assert.Equal(17, bytes.Length);
		Assert.Equal(3, bytes[0]);
		Assert.Equal(0, bytes[^1]);
	}

	[Fact]
	public void GetBytes_UsesProvidedEncoding()
	{
		var bytes = "abc".GetBytes(Encoding.UTF8);
		Assert.Equal(new byte[] { 97, 98, 99 }, bytes);
	}
}
