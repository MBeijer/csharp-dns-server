using Dns;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Models.Dns.Packets;
using Xunit;

namespace Dns.UnitTests;

public sealed class DnsRequestKeyTests
{
	[Fact]
	public void IsCaseInsensitiveAndHasStableString()
	{
		var keyA = new DnsRequestKey(12, ResourceClass.IN, ResourceType.A, "WWW.Example.Com");
		var keyB = new DnsRequestKey(12, ResourceClass.IN, ResourceType.A, "www.example.com");

		Assert.Equal(keyA, keyB);
		Assert.True(keyA == keyB);
		Assert.False(keyA != keyB);
		Assert.Contains("12|IN|A|", keyA.ToString());
	}

	[Fact]
	public void Constructor_FromDnsMessage_UsesFirstQuestion()
	{
		var message = new DnsMessage
		{
			QueryIdentifier = 99,
			QuestionCount = 1,
			Questions = [new Question("example.com", ResourceType.AAAA, ResourceClass.IN)],
		};

		var key = new DnsRequestKey(message);
		Assert.Equal((ushort)99, key.QueryId);
		Assert.Equal(ResourceType.AAAA, key.Type);
	}
}
