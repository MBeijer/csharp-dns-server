using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Models;
using Dns.Models.Dns.Packets;
using Dns.Models.Enums;
using Dns.RDataTypes;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dns.UnitTests;

public class DnsServerTests
{
	[Fact]
	public void BuildBasicResponse_CopiesRequestShape()
	{
		var request = new DnsMessage
		{
			QueryIdentifier = 0x1234,
			Opcode = (byte)OpCode.QUERY,
			RD = true,
		};
		request.Questions.Add(new Question("www.example.com", ResourceType.A, ResourceClass.IN));
		request.QuestionCount = 1;

		var response = InvokePrivateStatic<DnsMessage>(
			typeof(DnsServer),
			"BuildBasicResponse",
			request,
			(byte)RCode.NOERROR,
			true,
			false
		);

		Assert.True(response.QR);
		Assert.True(response.AA);
		Assert.False(response.RA);
		Assert.True(response.RD);
		Assert.Equal((byte)RCode.NOERROR, response.RCode);
		Assert.Equal((ushort)1, response.QuestionCount);
		Assert.Equal("www.example.com", response.Questions[0].Name);
	}

	[Fact]
	public void BuildRecordOwnerName_HandlesApexAbsoluteAndRelativeHosts()
	{
		Assert.Equal(
			"example.com",
			InvokePrivateStatic<string>(typeof(DnsServer), "BuildRecordOwnerName", "example.com", "")
		);
		Assert.Equal(
			"api.example.com",
			InvokePrivateStatic<string>(typeof(DnsServer), "BuildRecordOwnerName", "example.com", "api")
		);
		Assert.Equal(
			"api.example.com",
			InvokePrivateStatic<string>(typeof(DnsServer), "BuildRecordOwnerName", "example.com", "api.example.com.")
		);
	}

	[Fact]
	public void CanonicalZoneName_TrimsDots()
	{
		object nullArg = null;
		Assert.Equal("example.com", InvokePrivateStatic<string>(typeof(DnsServer), "CanonicalZoneName", ".example.com."));
		Assert.Equal(string.Empty, InvokePrivateStatic<string>(typeof(DnsServer), "CanonicalZoneName", nullArg));
	}

	[Fact]
	public void NotifyTargetParsing_HandlesIpAndIpPortEntries()
	{
		var parsed = InvokePrivateStatic<List<IPEndPoint>>(
			typeof(DnsServer),
			"ParseNotifyTargets",
			new List<string> { "192.0.2.1", "198.51.100.10:5353", "", "not-an-endpoint" }
		);

		Assert.Equal(2, parsed.Count);
		Assert.Equal(53, parsed[0].Port);
		Assert.Equal(5353, parsed[1].Port);
	}

	[Fact]
	public void IsAllowedByEntry_AndCidrMatching_CoversAclRules()
	{
		var address = IPAddress.Parse("10.1.2.3");
		Assert.True(InvokePrivateStatic<bool>(typeof(DnsServer), "IsAllowedByEntry", address, "*"));
		Assert.True(InvokePrivateStatic<bool>(typeof(DnsServer), "IsAllowedByEntry", address, "10.1.2.3"));
		Assert.True(InvokePrivateStatic<bool>(typeof(DnsServer), "IsAllowedByEntry", address, "10.1.0.0/16"));
		Assert.False(InvokePrivateStatic<bool>(typeof(DnsServer), "IsAllowedByEntry", address, "10.2.0.0/16"));
		Assert.False(InvokePrivateStatic<bool>(typeof(DnsServer), "IsAllowedByEntry", address, "invalid"));

		Assert.True(
			InvokePrivateStatic<bool>(
				typeof(DnsServer),
				"IsAddressInCidr",
				IPAddress.Parse("192.168.1.100"),
				IPAddress.Parse("192.168.1.0"),
				24
			)
		);
	}

	[Fact]
	public void CreateInjectedNsAddressRecord_HandlesAAndAaaaAndCname()
	{
		var serverIpv4 = CreateServer(injectedNsAddress: "192.0.2.53");
		var nsRecord = new ResourceRecord
		{
			Name = "example.com",
			Class = ResourceClass.IN,
			Type = ResourceType.NS,
			RData = new NSRData { Name = "ns1.example.com" },
		};

		var aRecord = InvokePrivate<ResourceRecord>(serverIpv4, "CreateInjectedNsAddressRecord", nsRecord, "example.com");
		Assert.Equal(ResourceType.A, aRecord.Type);
		Assert.Equal(IPAddress.Parse("192.0.2.53"), Assert.IsType<ANameRData>(aRecord.RData).Address);

		var serverIpv6 = CreateServer(injectedNsAddress: "2001:db8::53");
		var aaaaRecord = InvokePrivate<ResourceRecord>(serverIpv6, "CreateInjectedNsAddressRecord", nsRecord, "example.com");
		Assert.Equal(ResourceType.AAAA, aaaaRecord.Type);

		var serverHost = CreateServer(injectedNsAddress: "target");
		var cnameRecord = InvokePrivate<ResourceRecord>(serverHost, "CreateInjectedNsAddressRecord", nsRecord, "example.com");
		Assert.Equal(ResourceType.CNAME, cnameRecord.Type);
		Assert.Equal("target.example.com", Assert.IsType<CNameRData>(cnameRecord.RData).Name);
	}

	[Fact]
	public void BuildResourceRecords_CoversSupportedRecordTypes()
	{
		var server = CreateServer();
		var zone = new Zone { Suffix = "example.com", Serial = 3 };

		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord { Host = "", Type = ResourceType.NS, Class = ResourceClass.IN, Addresses = ["ns1.example.com"] },
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord { Host = "mail", Type = ResourceType.MX, Class = ResourceClass.IN, Addresses = ["10 mail.example.com"] },
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord { Host = "www", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.10"] },
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord { Host = "alias", Type = ResourceType.CNAME, Class = ResourceClass.IN, Addresses = ["www.example.com"] },
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord { Host = "txt", Type = ResourceType.TXT, Class = ResourceClass.IN, Addresses = ["hello"] },
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord { Host = "1", Type = ResourceType.PTR, Class = ResourceClass.IN, Addresses = ["host.example.com"] },
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord { Host = "", Type = ResourceType.SOA, Class = ResourceClass.IN, Addresses = ["ns1.example.com", "hostmaster.example.com"] },
				zone,
				"example.com"
			)
		);
	}

	[Fact]
	public void BuildResourceRecords_CnameAtShorthandTargetsResolveToZoneApex()
	{
		var server = CreateServer();
		var zone = new Zone { Suffix = "example.com", Serial = 3 };

		foreach (var alias in new[] { "@", "@.", "\\@", "\\@." })
		{
			var records = InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord { Host = "www", Type = ResourceType.CNAME, Class = ResourceClass.IN, Addresses = [alias] },
				zone,
				"example.com"
			);

			var cname = Assert.Single(records);
			Assert.Equal("example.com", Assert.IsType<CNameRData>(cname.RData).Name);
		}
	}

	[Fact]
	public void BuildAxfrRecords_AddsSoaEnvelopeAndFallbackNsWithInjectedAddress()
	{
		var server = CreateServer(injectedNsAddress: "192.0.2.53");
		var zone = new Zone { Suffix = "example.com", Serial = 11 };
		zone.Initialize(
			[
				new ZoneRecord { Host = "www", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.10"] },
			]
		);

		var records = InvokePrivate<List<ResourceRecord>>(server, "BuildAxfrRecords", zone, "example.com");

		Assert.Equal(ResourceType.SOA, records.First().Type);
		Assert.Equal(ResourceType.SOA, records.Last().Type);
		Assert.Contains(records, record => record.Type == ResourceType.NS && record.Name == "example.com");
		Assert.Contains(records, record => record.Type == ResourceType.A && record.Name.Contains("example.com"));
	}

	[Fact]
	public void BuildIxfrRecords_ReturnsSingleSoaWhenClientSerialIsCurrent()
	{
		var server = CreateServer();
		var zone = new Zone { Suffix = "example.com", Serial = 42 };
		var request = new DnsMessage { QuestionCount = 1 };
		request.Questions.Add(new Question("example.com", ResourceType.IXFR, ResourceClass.IN));
		request.Authorities.Add(
			new ResourceRecord
			{
				Name = "example.com",
				Class = ResourceClass.IN,
				Type = ResourceType.SOA,
				RData = new SOARData
				{
					PrimaryNameServer = "ns1.example.com",
					ResponsibleAuthoritativeMailbox = "hostmaster.example.com",
					Serial = 42,
					RefreshInterval = 300,
					RetryInterval = 300,
					ExpirationLimit = 86400,
					MinimumTTL = 300,
				},
			}
		);
		request.NameServerCount = 1;

		var records = InvokePrivate<List<ResourceRecord>>(server, "BuildIxfrRecords", request, zone, "example.com");

		Assert.Single(records);
		Assert.Equal(ResourceType.SOA, records[0].Type);
	}

	[Fact]
	public void BuildNotifyResponse_CoversFormerrNoerrorAndNotauth()
	{
		var zone = new Zone { Suffix = "example.com", Serial = 1 };
		var resolver = new FakeResolver([zone]);
		var server = CreateServer(resolvers: [resolver]);

		var empty = new DnsMessage();
		var formerr = InvokePrivate<DnsMessage>(server, "BuildNotifyResponse", empty, new IPEndPoint(IPAddress.Loopback, 53));
		Assert.Equal((byte)RCode.FORMERR, formerr.RCode);

		var notify = new DnsMessage { Opcode = (byte)OpCode.NOTIFY, QuestionCount = 1 };
		notify.Questions.Add(new Question("www.example.com", ResourceType.SOA, ResourceClass.IN));
		var ok = InvokePrivate<DnsMessage>(server, "BuildNotifyResponse", notify, new IPEndPoint(IPAddress.Loopback, 53));
		Assert.Equal((byte)RCode.NOERROR, ok.RCode);
		Assert.True(ok.AA);

		var missing = new DnsMessage { Opcode = (byte)OpCode.NOTIFY, QuestionCount = 1 };
		missing.Questions.Add(new Question("missing.invalid", ResourceType.SOA, ResourceClass.IN));
		var notauth = InvokePrivate<DnsMessage>(server, "BuildNotifyResponse", missing, new IPEndPoint(IPAddress.Loopback, 53));
		Assert.Equal((byte)RCode.NOTAUTH, notauth.RCode);
	}

	[Fact]
	public void BuildResponseForQuery_CoversQueryPaths()
	{
		var server = CreateServer(zoneTransferEnabled: false);

		var noQuestion = new DnsMessage();
		var formerr = InvokePrivate<DnsMessage>(server, "BuildResponseForQuery", noQuestion, new IPEndPoint(IPAddress.Loopback, 53), true);
		Assert.Equal((byte)RCode.FORMERR, formerr.RCode);

		var notImpMsg = new DnsMessage { Opcode = 15, QuestionCount = 1 };
		notImpMsg.Questions.Add(new Question("example.com", ResourceType.A, ResourceClass.IN));
		var notimp = InvokePrivate<DnsMessage>(server, "BuildResponseForQuery", notImpMsg, new IPEndPoint(IPAddress.Loopback, 53), true);
		Assert.Equal((byte)RCode.NOTIMP, notimp.RCode);

		var refusedMsg = new DnsMessage { Opcode = (byte)OpCode.QUERY, QuestionCount = 1 };
		refusedMsg.Questions.Add(new Question("example.com", ResourceType.A, ResourceClass.IN));
		var refused = InvokePrivate<DnsMessage>(server, "BuildResponseForQuery", refusedMsg, new IPEndPoint(IPAddress.Loopback, 53), true);
		Assert.Equal((byte)RCode.REFUSED, refused.RCode);
	}

	[Fact]
	public void BuildTransferResponse_HandlesRefusedNotauthAndSuccess()
	{
		var zone = new Zone { Suffix = "example.com", Serial = 7 };
		zone.Initialize([new ZoneRecord { Host = "www", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.7"] }]);
		var resolver = new FakeResolver([zone]);

		var deniedServer = CreateServer(zoneTransferEnabled: true, allowTransfersFrom: ["10.0.0.0/8"], resolvers: [resolver]);
		var message = new DnsMessage { QuestionCount = 1 };
		message.Questions.Add(new Question("example.com", ResourceType.AXFR, ResourceClass.IN));
		var denied = InvokePrivate<DnsMessage>(
			deniedServer,
			"BuildTransferResponse",
			message,
			message.Questions[0],
			new IPEndPoint(IPAddress.Parse("192.0.2.20"), 53),
			true
		);
		Assert.Equal((byte)RCode.REFUSED, denied.RCode);

		var notauthServer = CreateServer(zoneTransferEnabled: true, allowTransfersFrom: ["192.0.2.0/24"], resolvers: [new FakeResolver([])]);
		var notauth = InvokePrivate<DnsMessage>(
			notauthServer,
			"BuildTransferResponse",
			message,
			message.Questions[0],
			new IPEndPoint(IPAddress.Parse("192.0.2.20"), 53),
			true
		);
		Assert.Equal((byte)RCode.NOTAUTH, notauth.RCode);

		var okServer = CreateServer(zoneTransferEnabled: true, allowTransfersFrom: ["192.0.2.0/24"], resolvers: [resolver], injectedNsAddress: "192.0.2.53");
		var ok = InvokePrivate<DnsMessage>(
			okServer,
			"BuildTransferResponse",
			message,
			message.Questions[0],
			new IPEndPoint(IPAddress.Parse("192.0.2.20"), 53),
			true
		);
		Assert.Equal((byte)RCode.NOERROR, ok.RCode);
		Assert.True(ok.AnswerCount >= 3);
	}

	[Fact]
	public void TryResolveZone_CoversResolverLookup()
	{
		var server = CreateServer(resolvers: [new FakeResolver([new Zone { Suffix = "example.com", Serial = 1 }])]);

		var args = new object[] { "api.example.com", null };
		var found = (bool)typeof(DnsServer).GetMethod("TryResolveZone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(server, args)!;
		Assert.True(found);
		Assert.NotNull(args[1]);
	}

	[Fact]
	public async Task ProcessTcpRequest_InvalidAndValidPayloads()
	{
		var zone = new Zone { Suffix = "example.com", Serial = 3 };
		zone.Initialize([new ZoneRecord { Host = "www", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.1"] }]);
		var server = CreateServer(zoneTransferEnabled: true, allowTransfersFrom: ["127.0.0.1/32"], resolvers: [new FakeResolver([zone])]);

		var invalidTask = InvokePrivate<Task<byte[]>>(
			server,
			"ProcessTcpRequest",
			new byte[] { 0x1, 0x2, 0x3 },
			3,
			new IPEndPoint(IPAddress.Loopback, 53)
		);
		var invalidResult = await invalidTask;
		Assert.Null(invalidResult);

		var request = new DnsMessage { QueryIdentifier = 0x1111, QuestionCount = 1 };
		request.Questions.Add(new Question("example.com", ResourceType.AXFR, ResourceClass.IN));
		var payload = Serialize(request);
		var validTask = InvokePrivate<Task<byte[]>>(
			server,
			"ProcessTcpRequest",
			payload,
			payload.Length,
			new IPEndPoint(IPAddress.Loopback, 53)
		);
		var valid = await validTask;
		Assert.NotNull(valid);
		Assert.True(DnsMessage.TryParse(valid, out var response));
		Assert.Equal((byte)RCode.NOERROR, response.RCode);
	}

	[Fact]
	public void ProcessUdpRequest_InvalidPayloadDoesNotThrow()
	{
		var server = CreateServer(resolvers: [new FakeResolver([])]);

		InvokePrivateVoid(
			server,
			"ProcessUdpRequest",
			new byte[] { 0x1, 0x2, 0x3 },
			3,
			new IPEndPoint(IPAddress.Loopback, 53)
		);
	}

	[Fact]
	public void LifecycleAndStatusMethods_AreCovered()
	{
		var server = CreateServer(zoneTransferEnabled: true, allowTransfersFrom: ["127.0.0.1/32"], resolvers: [new FakeResolver([])]);

		server.Initialize([new FakeResolver([])]);

		using var cts = new CancellationTokenSource();
		server.Start(cts.Token);
		cts.Cancel();

		using var writer = new StringWriter();
		server.DumpHtml(writer);
		var html = writer.ToString();
		Assert.Contains("DNS Server Status", html);
		Assert.NotNull(server.GetObject());
	}

	[Fact]
	public void SendHelpers_AreCovered()
	{
		var server = CreateServer();
		var udpListener = new UdpListener();
		udpListener.Initialize(0);
		SetPrivateField(server, "_udpListener", udpListener);

		var zone = new Zone { Suffix = "example.com", Serial = 2 };
		InvokePrivateVoid(server, "SendNotify", zone, "example.com", new IPEndPoint(IPAddress.Loopback, 5302));

		var response = new DnsMessage { QueryIdentifier = 0xAAAA, QuestionCount = 1 };
		response.Questions.Add(new Question("example.com", ResourceType.A, ResourceClass.IN));
		InvokePrivateVoid(server, "SendUdpResponse", response, new IPEndPoint(IPAddress.Loopback, 5302));

		var args = new SocketAsyncEventArgs();
		InvokePrivateStaticVoid(typeof(DnsServer), "OnSendCompleted", null, args);
	}

	[Fact]
	public void ProcessUdpRequest_CoversNotifyAxfrAndQueryBranches()
	{
		var zone = new Zone { Suffix = "example.com", Serial = 9 };
		zone.Initialize(
			[
				new ZoneRecord { Host = "www", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.11"] },
				new ZoneRecord { Host = "www", Type = ResourceType.CNAME, Class = ResourceClass.IN, Addresses = ["external.invalid"] },
			]
		);

		var server = CreateServer(resolvers: [new FakeResolver([zone])], zoneTransferEnabled: true, allowTransfersFrom: ["127.0.0.1/32"]);
		var udpListener = new UdpListener();
		udpListener.Initialize(0);
		SetPrivateField(server, "_udpListener", udpListener);
		SetPrivateField(server, "_defaultDns", Array.Empty<IPAddress>());

		var remote = new IPEndPoint(IPAddress.Loopback, 5310);

		var notify = new DnsMessage
		{
			QueryIdentifier = 0x1001,
			Opcode = (byte)OpCode.NOTIFY,
			QuestionCount = 1,
		};
		notify.Questions.Add(new Question("www.example.com", ResourceType.SOA, ResourceClass.IN));
		var notifyBytes = Serialize(notify);
		InvokePrivateVoid(server, "ProcessUdpRequest", notifyBytes, notifyBytes.Length, remote);

		var udpAxfr = new DnsMessage
		{
			QueryIdentifier = 0x1002,
			QuestionCount = 1,
		};
		udpAxfr.Questions.Add(new Question("example.com", ResourceType.AXFR, ResourceClass.IN));
		var udpAxfrBytes = Serialize(udpAxfr);
		InvokePrivateVoid(server, "ProcessUdpRequest", udpAxfrBytes, udpAxfrBytes.Length, remote);

		var ptr = new DnsMessage
		{
			QueryIdentifier = 0x1003,
			QuestionCount = 1,
		};
		ptr.Questions.Add(new Question("1.0.0.127.in-addr.arpa", ResourceType.PTR, ResourceClass.IN));
		var ptrBytes = Serialize(ptr);
		InvokePrivateVoid(server, "ProcessUdpRequest", ptrBytes, ptrBytes.Length, remote);

		var queryExisting = new DnsMessage
		{
			QueryIdentifier = 0x1004,
			QuestionCount = 1,
		};
		queryExisting.Questions.Add(new Question("www.example.com", ResourceType.A, ResourceClass.IN));
		var queryExistingBytes = Serialize(queryExisting);
		InvokePrivateVoid(server, "ProcessUdpRequest", queryExistingBytes, queryExistingBytes.Length, remote);

		var queryMissingName = new DnsMessage
		{
			QueryIdentifier = 0x1005,
			QuestionCount = 1,
		};
		queryMissingName.Questions.Add(new Question("missing.example.com", ResourceType.A, ResourceClass.IN));
		var queryMissingBytes = Serialize(queryMissingName);
		InvokePrivateVoid(server, "ProcessUdpRequest", queryMissingBytes, queryMissingBytes.Length, remote);

		var queryMissingType = new DnsMessage
		{
			QueryIdentifier = 0x1006,
			QuestionCount = 1,
		};
		queryMissingType.Questions.Add(new Question("www.example.com", ResourceType.TXT, ResourceClass.IN));
		var queryMissingTypeBytes = Serialize(queryMissingType);
		InvokePrivateVoid(server, "ProcessUdpRequest", queryMissingTypeBytes, queryMissingTypeBytes.Length, remote);

		var unresolvedQuery = new DnsMessage
		{
			QueryIdentifier = 0x1007,
			QuestionCount = 1,
		};
		unresolvedQuery.Questions.Add(new Question("outside.invalid", ResourceType.A, ResourceClass.IN));
		var unresolvedBytes = Serialize(unresolvedQuery);
		InvokePrivateVoid(server, "ProcessUdpRequest", unresolvedBytes, unresolvedBytes.Length, remote);

		var upstreamResponse = new DnsMessage
		{
			QueryIdentifier = 0x1007,
			QR = true,
			QuestionCount = 1,
			AnswerCount = 1,
		};
		upstreamResponse.Questions.Add(new Question("outside.invalid", ResourceType.A, ResourceClass.IN));
		upstreamResponse.Answers.Add(
			new ResourceRecord
			{
				Name = "outside.invalid",
				Class = ResourceClass.IN,
				Type = ResourceType.A,
				TTL = 30,
				RData = new ANameRData { Address = IPAddress.Parse("203.0.113.1") },
			}
		);
		var upstreamResponseBytes = Serialize(upstreamResponse);
		InvokePrivateVoid(server, "ProcessUdpRequest", upstreamResponseBytes, upstreamResponseBytes.Length, remote);
	}

	[Fact]
	public void HandleRecords_CoversAllSupportedTypesWithoutRecursiveCnamePath()
	{
		var server = CreateServer();
		var message = new DnsMessage();
		var zone = new Zone { Suffix = "example.com", Serial = 5 };

		var zoneRecords = new List<ZoneRecord>
		{
			new() { Host = "ns", Type = ResourceType.NS, Class = ResourceClass.IN, Addresses = ["ns1.example.com"] },
			new() { Host = "mx", Type = ResourceType.MX, Class = ResourceClass.IN, Addresses = ["10 mail.example.com"] },
			new() { Host = "a", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.1"] },
			new() { Host = "alias", Type = ResourceType.CNAME, Class = ResourceClass.IN, Addresses = ["external.invalid"] },
			new() { Host = "soa", Type = ResourceType.SOA, Class = ResourceClass.IN, Addresses = ["ns1.example.com", "hostmaster.example.com"] },
			new() { Host = "txt", Type = ResourceType.TXT, Class = ResourceClass.IN, Addresses = ["v=spf1 -all"] },
			new() { Host = "ptr", Type = ResourceType.PTR, Class = ResourceClass.IN, Addresses = ["host.example.com"] },
		};

		InvokePrivateVoid(
			server,
			"HandleRecords",
			zoneRecords,
			new Question("www.example.com", ResourceType.ANY, ResourceClass.IN),
			message,
			zone,
			new IPEndPoint(IPAddress.Loopback, 5300)
		);

		Assert.True(message.AnswerCount >= 7);
		Assert.Contains(message.Answers, answer => answer.Type == ResourceType.SOA);
	}

	private static DnsServer CreateServer(
		bool zoneTransferEnabled = true,
		List<string> allowTransfersFrom = null,
		List<IDnsResolver> resolvers = null,
		string injectedNsAddress = null
	)
	{
		var options = new ServerOptions
		{
			DnsListener = new DnsListenerOptions { Port = 5301, TcpPort = 5301 },
			ZoneTransfer = new ZoneTransferOptions
			{
				Enabled = zoneTransferEnabled,
				AllowTransfersFrom = allowTransfersFrom ?? ["127.0.0.1/32"],
				NotifySecondaries = [],
				InjectedNsAddress = injectedNsAddress,
			},
			WebServer = new WebServerOptions(),
		};

		var server = new DnsServer(new FakeLogger<DnsServer>(), Options.Create(options));

		typeof(DnsServer)
			.GetField("_resolvers", BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(server, resolvers ?? []);

		return server;
	}

	private static byte[] Serialize(DnsMessage message)
	{
		using var ms = new MemoryStream();
		message.WriteToStream(ms);
		return ms.ToArray();
	}

	private static T InvokePrivate<T>(object instance, string methodName, params object[] args)
	{
		var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
		return (T)method.Invoke(instance, args)!;
	}

	private static void InvokePrivateVoid(object instance, string methodName, params object[] args)
	{
		var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
		method.Invoke(instance, args);
	}

	private static void InvokePrivateStaticVoid(Type type, string methodName, params object[] args)
	{
		var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
		method.Invoke(null, args);
	}

	private static T InvokePrivateStatic<T>(Type type, string methodName, params object[] args)
	{
		var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
		return (T)method.Invoke(null, args)!;
	}

	private static void SetPrivateField(object instance, string fieldName, object value)
	{
		instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
	}

	private sealed class FakeResolver(List<Zone> zones) : IDnsResolver
	{
		private readonly List<Zone> _zones = zones;

		public bool TryGetZone(string hostname, out Zone zone)
		{
			zone = _zones.FirstOrDefault(z => hostname.EndsWith(z.Suffix, StringComparison.OrdinalIgnoreCase));
			return zone != null;
		}

		public IEnumerable<Zone> GetZones() => _zones;

		public void SubscribeTo(IObservable<List<Zone>> zoneProvider)
		{
		}

		public void DumpHtml(TextWriter writer)
		{
		}

		public object GetObject() => _zones;

		public void OnCompleted()
		{
		}

		public void OnError(Exception error)
		{
		}

		public void OnNext(List<Zone> value)
		{
		}
	}
}