using System.IO;
using System.Reflection;
using System.Linq;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Models.Dns.Packets;
using Dns.RDataTypes;
using Xunit;

namespace Dns.UnitTests;

public sealed class RDataLengthTests
{
	[Theory]
	[InlineData("target.example.com")]
	[InlineData("target.example.com.")]
	public void DomainNameRData_LengthMatchesBytesWritten(string name)
	{
		AssertLengthMatches(new CNameRData { Name           = name });
		AssertLengthMatches(new DomainNamePointRData { Name = name });
		AssertLengthMatches(new NSRData { Name              = name });
	}

	[Theory]
	[InlineData("ns1.example.com", "hostmaster.example.com")]
	[InlineData("ns1.example.com.", "hostmaster.example.com.")]
	public void SoaRData_LengthMatchesBytesWritten(string primaryNameServer, string mailbox)
	{
		AssertLengthMatches(
			new SOARData
			{
				PrimaryNameServer               = primaryNameServer,
				ResponsibleAuthoritativeMailbox = mailbox,
				Serial                          = 2026081601,
				RefreshInterval                 = 7200,
				RetryInterval                   = 1800,
				ExpirationLimit                 = 1209600,
				MinimumTTL                      = 3600,
			}
		);
	}

	[Theory]
	[InlineData("mail.example.com")]
	[InlineData("mail.example.com.")]
	public void MxRData_LengthMatchesBytesWritten(string exchange)
	{
		var payload = new byte[]
		{
			0,
			10,
			4,
			(byte)'m',
			(byte)'a',
			(byte)'i',
			(byte)'l',
			7,
			(byte)'e',
			(byte)'x',
			(byte)'a',
			(byte)'m',
			(byte)'p',
			(byte)'l',
			(byte)'e',
			3,
			(byte)'c',
			(byte)'o',
			(byte)'m',
			0,
		};
		var mx = MXRData.Parse(payload, 0, payload.Length);
		typeof(MXRData).GetProperty("Name", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mx, exchange);

		AssertLengthMatches(mx);
	}

	[Theory]
	[InlineData("example.com", 13)]
	[InlineData("example.com.", 13)]
	[InlineData(".", 1)]
	public void DomainNameWireLength_IgnoresPresentationTrailingDot(string name, ushort expected)
	{
		Assert.Equal(expected, DnsProtocol.GetDomainNameWireLength(name));
	}

	[Fact]
	public void AxfrStyleMessage_WithTrailingDotRData_ParsesWithoutExtraInput()
	{
		var soa = new SOARData
		{
			PrimaryNameServer               = "ns1.example.com.",
			ResponsibleAuthoritativeMailbox = "hostmaster.example.com.",
			Serial                          = 2026081601,
			RefreshInterval                 = 7200,
			RetryInterval                   = 1800,
			ExpirationLimit                 = 1209600,
			MinimumTTL                      = 3600,
		};
		var message = new DnsMessage
		{
			QueryIdentifier = 0x1234,
			QR              = true,
			AA              = true,
			QuestionCount   = 1,
			AnswerCount     = 3,
		};
		message.Questions.Add(new("example.com", ResourceType.AXFR, ResourceClass.IN));
		message.Answers.Add(CreateRecord("example.com", ResourceType.SOA, soa));
		message.Answers.Add(
			CreateRecord("www.example.com", ResourceType.CNAME, new CNameRData { Name = "target.example.com." })
		);
		message.Answers.Add(CreateRecord("example.com", ResourceType.SOA, soa));

		using var stream = new MemoryStream();
		message.WriteToStream(stream);
		var payload = stream.ToArray();

		Assert.True(DnsMessage.TryParse(payload, payload.Length, out var parsed));
		Assert.Equal(3, parsed.Answers.Count);

		var payloadWithExtraByte = payload.Append((byte)0).ToArray();
		Assert.False(DnsMessage.TryParse(payloadWithExtraByte, payloadWithExtraByte.Length, out _));
	}

	private static void AssertLengthMatches(RData rData)
	{
		using var stream = new MemoryStream();
		rData.WriteToStream(stream);
		Assert.Equal(stream.Length, rData.Length);
	}

	private static ResourceRecord CreateRecord(string name, ResourceType type, RData data) =>
		new()
		{
			Name  = name,
			Type  = type,
			Class = ResourceClass.IN,
			TTL   = 300,
			RData = data,
		};
}