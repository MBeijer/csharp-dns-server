using System.IO;
using Dns.RDataTypes;
using Xunit;

namespace Dns.UnitTests;

public sealed class NSRDataTests
{
	[Fact]
	public void Length_MatchesEncodedBytes_WithTrailingDot()
	{
		var target = new NSRData { Name = "ns1.graalonline.com." };
		using var stream = new MemoryStream();

		target.WriteToStream(stream);
		var encoded = stream.ToArray();

		Assert.Equal((int)target.Length, encoded.Length);
		Assert.Equal(21, encoded.Length);
		Assert.Equal(0, encoded[^1]);
		Assert.NotEqual(0, encoded[^2]);
	}
}
