using Dns;
using Dns.Db.Models.EntityFramework.Enums;
using Xunit;

namespace Dns.UnitTests;

public sealed class DnsZoneLookupKeyTests
{
	[Fact]
	public void IsCaseInsensitive()
	{
		var keyA = new DnsZoneLookupKey("WWW", ResourceClass.IN, ResourceType.CNAME);
		var keyB = new DnsZoneLookupKey("www", ResourceClass.IN, ResourceType.CNAME);
		Assert.Equal(keyA, keyB);
		Assert.Equal(keyA.GetHashCode(), keyB.GetHashCode());
	}
}