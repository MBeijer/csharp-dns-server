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
using Dns.ZoneProvider.Secondary;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dns.UnitTests;

public class DnsServerTests
{
	[Fact]
	public void BuildBasicResponse_CopiesRequestShape()
	{
		var request = new DnsMessage { QueryIdentifier = 0x1234, Opcode = (byte)OpCode.QUERY, RD = true, };
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
		Assert.Equal(
			"example.com",
			InvokePrivateStatic<string>(typeof(DnsServer), "CanonicalZoneName", ".example.com.")
		);
		Assert.Equal(string.Empty, InvokePrivateStatic<string>(typeof(DnsServer), "CanonicalZoneName", nullArg));
	}

	[Fact]
	public void NotifyTargetParsing_HandlesIpHostnameAndOptionalPortEntries()
	{
		var server = CreateServer();
		SetPrivateField(
			server,
			"_hostAddressResolver",
			(Func<string, IPAddress[]>)(_ =>
			{
				return [IPAddress.Parse("203.0.113.53"), IPAddress.Parse("2001:db8::53")];
			})
		);
		var parsed = InvokePrivate<List<IPEndPoint>>(
			server,
			"ParseNotifyTargets",
			new List<string>
			{
				"192.0.2.1",
				"198.51.100.10:5353",
				"[2001:db8::10]:5354",
				"notify.example:5355",
				"",
				"not an endpoint",
			}
		);

		Assert.Equal(5, parsed.Count);
		Assert.Equal(53, parsed[0].Port);
		Assert.Equal(5353, parsed[1].Port);
		Assert.Equal(IPAddress.Parse("2001:db8::10"), parsed[2].Address);
		Assert.Equal(5354, parsed[2].Port);
		Assert.Equal(IPAddress.Parse("203.0.113.53"), parsed[3].Address);
		Assert.Equal(5355, parsed[3].Port);
		Assert.Equal(IPAddress.Parse("2001:db8::53"), parsed[4].Address);
		Assert.Equal(5355, parsed[4].Port);
	}

	[Fact]
	public void IsAllowedByEntry_AndCidrMatching_CoversAclRules()
	{
		var server  = CreateServer();
		var address = IPAddress.Parse("10.1.2.3");
		Assert.True(InvokePrivate<bool>(server, "IsAllowedByEntry", address, "*"));
		Assert.True(InvokePrivate<bool>(server, "IsAllowedByEntry", address, "10.1.2.3"));
		Assert.True(InvokePrivate<bool>(server, "IsAllowedByEntry", address, "10.1.0.0/16"));
		Assert.True(InvokePrivate<bool>(server, "IsAllowedByEntry", IPAddress.Parse("::ffff:10.1.2.3"), "10.1.0.0/16"));
		Assert.False(InvokePrivate<bool>(server, "IsAllowedByEntry", address, "10.2.0.0/16"));
		Assert.False(InvokePrivate<bool>(server, "IsAllowedByEntry", address, "not an endpoint"));

		Assert.True(
			InvokePrivateStatic<bool>(
				typeof(DnsServer),
				"IsAddressInCidr",
				IPAddress.Parse("192.168.1.100"),
				IPAddress.Parse("192.168.1.0"),
				24
			)
		);
		Assert.False(
			InvokePrivateStatic<bool>(
				typeof(DnsServer),
				"IsAddressInCidr",
				IPAddress.Parse("192.168.1.100"),
				IPAddress.Parse("192.168.1.0"),
				33
			)
		);
	}

	[Fact]
	public void HostnameAcl_AllowsResolvedIpv4AndIpv6Addresses()
	{
		var server          = CreateServer();
		var resolutionCount = 0;
		SetPrivateField(
			server,
			"_hostAddressResolver",
			(Func<string, IPAddress[]>)(_ =>
			{
				resolutionCount++;
				return [IPAddress.Parse("192.0.2.53"), IPAddress.Parse("2001:db8::53")];
			})
		);

		Assert.True(InvokePrivate<bool>(server, "IsAllowedByEntry", IPAddress.Parse("192.0.2.53"), "ns2.afraid.org"));
		Assert.True(
			InvokePrivate<bool>(server, "IsAllowedByEntry", IPAddress.Parse("::ffff:192.0.2.53"), "ns2.afraid.org.")
		);
		Assert.True(InvokePrivate<bool>(server, "IsAllowedByEntry", IPAddress.Parse("2001:db8::53"), "ns2.afraid.org"));
		Assert.False(
			InvokePrivate<bool>(server, "IsAllowedByEntry", IPAddress.Parse("2001:db8::54"), "ns2.afraid.org")
		);
		Assert.Equal(1, resolutionCount);
	}

	[Fact]
	public void CreateInjectedNsAddressRecord_HandlesAAndAaaaAndCname()
	{
		var serverIpv4 = CreateServer(injectedNsAddress: "192.0.2.53");
		var nsRecord = new ResourceRecord
		{
			Name  = "example.com",
			Class = ResourceClass.IN,
			Type  = ResourceType.NS,
			RData = new NSRData { Name = "ns1.example.com" },
		};

		var aRecord = InvokePrivate<ResourceRecord>(
			serverIpv4,
			"CreateInjectedNsAddressRecord",
			nsRecord,
			"example.com"
		);
		Assert.Equal(ResourceType.A, aRecord.Type);
		Assert.Equal(IPAddress.Parse("192.0.2.53"), Assert.IsType<ANameRData>(aRecord.RData).Address);

		var serverIpv6 = CreateServer(injectedNsAddress: "2001:db8::53");
		var aaaaRecord = InvokePrivate<ResourceRecord>(
			serverIpv6,
			"CreateInjectedNsAddressRecord",
			nsRecord,
			"example.com"
		);
		Assert.Equal(ResourceType.AAAA, aaaaRecord.Type);

		var serverHost = CreateServer(injectedNsAddress: "target");
		var cnameRecord = InvokePrivate<ResourceRecord>(
			serverHost,
			"CreateInjectedNsAddressRecord",
			nsRecord,
			"example.com"
		);
		Assert.Equal(ResourceType.CNAME, cnameRecord.Type);
		Assert.Equal("target.example.com", Assert.IsType<CNameRData>(cnameRecord.RData).Name);
	}

	[Fact]
	public void BuildResourceRecords_CoversSupportedRecordTypes()
	{
		var server = CreateServer();
		var zone   = new Zone { Suffix = "example.com", Serial = 3 };

		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord
				{
					Host = "", Type = ResourceType.NS, Class = ResourceClass.IN, Addresses = ["ns1.example.com"]
				},
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord
				{
					Host      = "mail",
					Type      = ResourceType.MX,
					Class     = ResourceClass.IN,
					Addresses = ["10 mail.example.com"]
				},
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord
				{
					Host = "www", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.10"]
				},
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord
				{
					Host      = "alias",
					Type      = ResourceType.CNAME,
					Class     = ResourceClass.IN,
					Addresses = ["www.example.com"]
				},
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord
				{
					Host = "txt", Type = ResourceType.TXT, Class = ResourceClass.IN, Addresses = ["hello"]
				},
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord
				{
					Host = "1", Type = ResourceType.PTR, Class = ResourceClass.IN, Addresses = ["host.example.com"]
				},
				zone,
				"example.com"
			)
		);
		Assert.Single(
			InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord
				{
					Host      = "",
					Type      = ResourceType.SOA,
					Class     = ResourceClass.IN,
					Addresses = ["ns1.example.com", "hostmaster.example.com"]
				},
				zone,
				"example.com"
			)
		);
	}

	[Fact]
	public void BuildResourceRecords_CnameAtShorthandTargetsResolveToZoneApex()
	{
		var server = CreateServer();
		var zone   = new Zone { Suffix = "example.com", Serial = 3 };

		foreach (var alias in new[] { "@", "@.", "\\@", "\\@." })
		{
			var records = InvokePrivate<List<ResourceRecord>>(
				server,
				"BuildResourceRecords",
				new ZoneRecord
				{
					Host = "www", Type = ResourceType.CNAME, Class = ResourceClass.IN, Addresses = [alias]
				},
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
		var zone   = new Zone { Suffix = "example.com", Serial = 11 };
		zone.Initialize(
			[
				new ZoneRecord
				{
					Host = "www", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.10"]
				},
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
		var server  = CreateServer();
		var zone    = new Zone { Suffix              = "example.com", Serial = 42 };
		var request = new DnsMessage { QuestionCount = 1 };
		request.Questions.Add(new Question("example.com", ResourceType.IXFR, ResourceClass.IN));
		request.Authorities.Add(
			new ResourceRecord
			{
				Name  = "example.com",
				Class = ResourceClass.IN,
				Type  = ResourceType.SOA,
				RData = new SOARData
				{
					PrimaryNameServer               = "ns1.example.com",
					ResponsibleAuthoritativeMailbox = "hostmaster.example.com",
					Serial                          = 42,
					RefreshInterval                 = 300,
					RetryInterval                   = 300,
					ExpirationLimit                 = 86400,
					MinimumTTL                      = 300,
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
		var zone     = new Zone { Suffix = "example.com", Serial = 1 };
		var resolver = new FakeResolver([zone]);
		var server   = CreateServer(resolvers: [resolver]);

		var empty = new DnsMessage();
		var formerr = InvokePrivate<DnsMessage>(
			server,
			"BuildNotifyResponse",
			empty,
			new IPEndPoint(IPAddress.Loopback, 53)
		);
		Assert.Equal((byte)RCode.FORMERR, formerr.RCode);

		var notify = new DnsMessage { Opcode = (byte)OpCode.NOTIFY, QuestionCount = 1 };
		notify.Questions.Add(new Question("www.example.com", ResourceType.SOA, ResourceClass.IN));
		var ok = InvokePrivate<DnsMessage>(
			server,
			"BuildNotifyResponse",
			notify,
			new IPEndPoint(IPAddress.Loopback, 53)
		);
		Assert.Equal((byte)RCode.NOERROR, ok.RCode);
		Assert.True(ok.AA);

		var missing = new DnsMessage { Opcode = (byte)OpCode.NOTIFY, QuestionCount = 1 };
		missing.Questions.Add(new Question("missing.invalid", ResourceType.SOA, ResourceClass.IN));
		var notauth = InvokePrivate<DnsMessage>(
			server,
			"BuildNotifyResponse",
			missing,
			new IPEndPoint(IPAddress.Loopback, 53)
		);
		Assert.Equal((byte)RCode.NOTAUTH, notauth.RCode);
	}

	[Fact]
	public void BuildResponseForQuery_CoversQueryPaths()
	{
		var server = CreateServer(zoneTransferEnabled: false);

		var noQuestion = new DnsMessage();
		var formerr = InvokePrivate<DnsMessage>(
			server,
			"BuildResponseForQuery",
			noQuestion,
			new IPEndPoint(IPAddress.Loopback, 53),
			true
		);
		Assert.Equal((byte)RCode.FORMERR, formerr.RCode);

		var notImpMsg = new DnsMessage { Opcode = 15, QuestionCount = 1 };
		notImpMsg.Questions.Add(new Question("example.com", ResourceType.A, ResourceClass.IN));
		var notimp = InvokePrivate<DnsMessage>(
			server,
			"BuildResponseForQuery",
			notImpMsg,
			new IPEndPoint(IPAddress.Loopback, 53),
			true
		);
		Assert.Equal((byte)RCode.NOTIMP, notimp.RCode);

		var refusedMsg = new DnsMessage { Opcode = (byte)OpCode.QUERY, QuestionCount = 1 };
		refusedMsg.Questions.Add(new Question("example.com", ResourceType.A, ResourceClass.IN));
		var refused = InvokePrivate<DnsMessage>(
			server,
			"BuildResponseForQuery",
			refusedMsg,
			new IPEndPoint(IPAddress.Loopback, 53),
			true
		);
		Assert.Equal((byte)RCode.REFUSED, refused.RCode);
	}

	[Fact]
	public void BuildTransferResponse_HandlesRefusedNotauthAndSuccess()
	{
		var zone = new Zone { Suffix = "example.com", Serial = 7 };
		zone.Initialize(
			[
				new ZoneRecord
					{
						Host = "www", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.7"]
					}
			]
		);
		var resolver = new FakeResolver([zone]);

		var deniedServer = CreateServer(
			zoneTransferEnabled: true,
			allowTransfersFrom: ["10.0.0.0/8"],
			resolvers: [resolver]
		);
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

		var notauthServer = CreateServer(
			zoneTransferEnabled: true,
			allowTransfersFrom: ["192.0.2.0/24"],
			resolvers: [new FakeResolver([])]
		);
		var notauth = InvokePrivate<DnsMessage>(
			notauthServer,
			"BuildTransferResponse",
			message,
			message.Questions[0],
			new IPEndPoint(IPAddress.Parse("192.0.2.20"), 53),
			true
		);
		Assert.Equal((byte)RCode.NOTAUTH, notauth.RCode);

		var okServer = CreateServer(
			zoneTransferEnabled: true,
			allowTransfersFrom: ["192.0.2.0/24"],
			resolvers: [resolver],
			injectedNsAddress: "192.0.2.53"
		);
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
		var found = (bool)typeof(DnsServer).GetMethod("TryResolveZone", BindingFlags.Instance | BindingFlags.NonPublic)!
		                                   .Invoke(server, args)!;
		Assert.True(found);
		Assert.NotNull(args[1]);
	}

	[Fact]
	public async Task ProcessTcpRequest_InvalidAndValidPayloads()
	{
		var zone = new Zone { Suffix = "example.com", Serial = 3 };
		zone.Initialize(
			[
				new ZoneRecord
					{
						Host = "www", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.1"]
					}
			]
		);
		var server = CreateServer(
			zoneTransferEnabled: true,
			allowTransfersFrom: ["127.0.0.1/32"],
			resolvers: [new FakeResolver([zone])]
		);

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
	public async Task CatalogStream_SendsFullSnapshotToMultipleSubscribersAndReconnects()
	{
		var zones = new List<Zone>
		{
			new() { Suffix = "example.com", Serial = 11 }, new() { Suffix = "example.net", Serial = 22 },
		};
		var resolver = new FakeResolver(zones);
		var server = CreateServer(
			zoneTransferEnabled: true,
			allowTransfersFrom: ["127.0.0.1/32"],
			resolvers: [resolver]
		);
		var request = new DnsMessage { QueryIdentifier = 0x5151, QuestionCount = 1 };
		request.Questions.Add(new Question(DnsServer.CatalogZoneName, ResourceType.AXFR, ResourceClass.IN));
		var payload = Serialize(request);
		var remote  = new IPEndPoint(IPAddress.Loopback, 5300);

		var firstStream = InvokePrivate<IAsyncEnumerable<byte[]>>(
			server,
			"ProcessTcpStreamRequest",
			payload,
			payload.Length,
			remote,
			CancellationToken.None
		);
		var secondStream = InvokePrivate<IAsyncEnumerable<byte[]>>(
			server,
			"ProcessTcpStreamRequest",
			payload,
			payload.Length,
			remote,
			CancellationToken.None
		);

		await using var first  = firstStream.GetAsyncEnumerator();
		await using var second = secondStream.GetAsyncEnumerator();
		Assert.True(await first.MoveNextAsync());
		Assert.True(await second.MoveNextAsync());
		AssertCatalogSnapshot(first.Current, 11, 22);
		AssertCatalogSnapshot(second.Current, 11, 22);

		InvokePrivateVoid(server, "OnResolverZonesChanged", resolver, EventArgs.Empty);
		Assert.True(await first.MoveNextAsync());
		Assert.True(await second.MoveNextAsync());

		var reconnectStream = InvokePrivate<IAsyncEnumerable<byte[]>>(
			server,
			"ProcessTcpStreamRequest",
			payload,
			payload.Length,
			remote,
			CancellationToken.None
		);
		await using var reconnect = reconnectStream.GetAsyncEnumerator();
		Assert.True(await reconnect.MoveNextAsync());
		AssertCatalogSnapshot(reconnect.Current, 11, 22);
	}

	[Fact]
	public async Task CatalogStream_WaitsForInitialResolverSnapshotAndAcceptsLoadedEmptyResolver()
	{
		var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var server = CreateServer(
			zoneTransferEnabled: true,
			allowTransfersFrom: ["127.0.0.1/32"],
			resolvers: [resolver]
		);
		var request = new DnsMessage { QueryIdentifier = 0x5252, QuestionCount = 1 };
		request.Questions.Add(new Question(DnsServer.CatalogZoneName, ResourceType.AXFR, ResourceClass.IN));
		var payload = Serialize(request);
		var stream = InvokePrivate<IAsyncEnumerable<byte[]>>(
			server,
			"ProcessTcpStreamRequest",
			payload,
			payload.Length,
			new IPEndPoint(IPAddress.Loopback, 5300),
			CancellationToken.None
		);

		await using var enumerator    = stream.GetAsyncEnumerator();
		var             firstSnapshot = enumerator.MoveNextAsync().AsTask();
		await Task.Delay(100);
		Assert.False(firstSnapshot.IsCompleted);

		((IObserver<List<Zone>>)resolver).OnNext([]);

		Assert.True(await firstSnapshot.WaitAsync(TimeSpan.FromSeconds(5)));
		AssertCatalogSnapshot(enumerator.Current);
	}

	[Fact]
	public async Task SecondarySync_ReplicatesInitialAndChangedZoneToMultipleSecondaries()
	{
		var port            = GetAvailableTcpPort();
		var primaryResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var primaryZone     = CreateReplicationZone(1, "192.0.2.10");
		((IObserver<List<Zone>>)primaryResolver).OnNext([primaryZone]);
		var primaryOptions = Options.Create(
			new ServerOptions
			{
				DnsListener  = new DnsListenerOptions { Port     = port, TcpPort            = port },
				ZoneTransfer = new ZoneTransferOptions { Enabled = true, AllowTransfersFrom = ["127.0.0.1/32"], },
				WebServer    = new WebServerOptions(),
			}
		);
		var primary = new DnsServer(new FakeLogger<DnsServer>(), primaryOptions);
		primary.Initialize([primaryResolver]);
		using var primaryCts = new CancellationTokenSource();
		await primary.Start(primaryCts.Token);

		var       firstResolver   = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var       secondResolver  = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		using var firstSecondary  = CreateSecondaryProvider(firstResolver, port);
		using var secondSecondary = CreateSecondaryProvider(secondResolver, port);
		firstSecondary.Start(CancellationToken.None);
		secondSecondary.Start(CancellationToken.None);

		await WaitForZoneSerialAsync(firstResolver, 1);
		await WaitForZoneSerialAsync(secondResolver, 1);
		Assert.Contains(
			firstResolver.GetZones().Single(zone => zone.Suffix == "replicated.example").Records,
			record => record.Type == ResourceType.TXT && record.Addresses.Single() == new string('x', 200)
		);

		primaryZone = CreateReplicationZone(2, "192.0.2.20");
		((IObserver<List<Zone>>)primaryResolver).OnNext([primaryZone]);

		var firstUpdated  = await WaitForZoneSerialAsync(firstResolver, 2);
		var secondUpdated = await WaitForZoneSerialAsync(secondResolver, 2);
		Assert.Equal(
			"192.0.2.20",
			firstUpdated.Records.Single(record => record.Type == ResourceType.A).Addresses.Single()
		);
		Assert.Equal(
			"192.0.2.20",
			secondUpdated.Records.Single(record => record.Type == ResourceType.A).Addresses.Single()
		);
		await primaryCts.CancelAsync();

		var restartedResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		((IObserver<List<Zone>>)restartedResolver).OnNext(
			[
				CreateReplicationZone(3, "192.0.2.30"),
				CreateReplicationZone(1, "198.51.100.10", "new.example"),
			]
		);
		var restartedPrimary = new DnsServer(new FakeLogger<DnsServer>(), primaryOptions);
		restartedPrimary.Initialize([restartedResolver]);
		using var restartedCts = new CancellationTokenSource();
		await restartedPrimary.Start(restartedCts.Token);

		await WaitForZoneSerialAsync(firstResolver, 3);
		await WaitForZoneSerialAsync(secondResolver, 3);
		await WaitForZoneCountAsync(firstResolver, 2);
		await WaitForZoneCountAsync(secondResolver, 2);
		Assert.Contains(firstResolver.GetZones(), zone => zone.Suffix == "new.example");
		Assert.Contains(secondResolver.GetZones(), zone => zone.Suffix == "new.example");

		await restartedCts.CancelAsync();
	}

	[Fact]
	public async Task SecondarySync_RetainsLastKnownGoodZoneWhilePrimaryIsDisconnected()
	{
		var port            = GetAvailableTcpPort();
		var cacheFile       = Path.Combine(Path.GetTempPath(), $"secondary-zones-{Guid.NewGuid():N}.json");
		var primaryResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		((IObserver<List<Zone>>)primaryResolver).OnNext([CreateReplicationZone(12, "192.0.2.12", "retained.example")]);
		var       primary    = CreateReplicationPrimary(port, primaryResolver);
		using var primaryCts = new CancellationTokenSource();
		await primary.Start(primaryCts.Token);

		var secondaryResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var secondary = CreateSecondaryProvider(secondaryResolver, port, settings => settings.CacheFile = cacheFile);
		secondary.Start(CancellationToken.None);

		try
		{
			var transferred = await WaitForNamedZoneAsync(
				secondaryResolver,
				"retained.example",
				TimeSpan.FromSeconds(10)
			);
			Assert.Equal((uint)12, transferred.Serial);
			var cachedSnapshot = await WaitForFileContainingAsync(
				cacheFile,
				"retained.example",
				TimeSpan.FromSeconds(5)
			);
			var lastZoneReload = secondaryResolver.LastZoneReload;

			await primaryCts.CancelAsync();
			await Task.Delay(TimeSpan.FromSeconds(3));

			var retained = Assert.Single(secondaryResolver.GetZones());
			Assert.Equal("retained.example", retained.Suffix);
			Assert.Equal((uint)12, retained.Serial);
			Assert.Equal(lastZoneReload, secondaryResolver.LastZoneReload);
			Assert.Equal(cachedSnapshot, await File.ReadAllTextAsync(cacheFile));
		}
		finally
		{
			await primaryCts.CancelAsync();
			secondary.Dispose();
			File.Delete(cacheFile);
			File.Delete($"{cacheFile}.tmp");
		}
	}

	[Fact]
	public async Task SecondarySync_WaitsForRestartedPrimaryReadinessBeforeRemovingCatalogDelta()
	{
		var port            = GetAvailableTcpPort();
		var primaryResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		((IObserver<List<Zone>>)primaryResolver).OnNext(
			[
				CreateReplicationZone(1, "192.0.2.10", "retained.example"),
				CreateReplicationZone(1, "192.0.2.20", "removed.example"),
			]
		);
		var       primary    = CreateReplicationPrimary(port, primaryResolver);
		using var primaryCts = new CancellationTokenSource();
		await primary.Start(primaryCts.Token);

		var       secondaryResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		using var secondary         = CreateSecondaryProvider(secondaryResolver, port);
		secondary.Start(CancellationToken.None);
		await WaitForZoneCountAsync(secondaryResolver, 2);

		await primaryCts.CancelAsync();

		var       restartedResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var       restartedPrimary  = CreateReplicationPrimary(port, restartedResolver);
		using var restartedCts      = new CancellationTokenSource();
		await restartedPrimary.Start(restartedCts.Token);

		try
		{
			var reloadBeforePrimaryReady = secondaryResolver.LastZoneReload;
			await Task.Delay(TimeSpan.FromSeconds(3));

			Assert.Equal(2, secondaryResolver.GetZones().Count());
			Assert.Contains(secondaryResolver.GetZones(), zone => zone.Suffix == "retained.example");
			Assert.Contains(secondaryResolver.GetZones(), zone => zone.Suffix == "removed.example");
			Assert.Equal(reloadBeforePrimaryReady, secondaryResolver.LastZoneReload);

			((IObserver<List<Zone>>)restartedResolver).OnNext(
				[CreateReplicationZone(2, "192.0.2.11", "retained.example")]
			);

			await WaitForZoneCountAsync(secondaryResolver, 1);
			var retained = await WaitForNamedZoneSerialAsync(
				secondaryResolver,
				"retained.example",
				2,
				TimeSpan.FromSeconds(10)
			);
			Assert.Equal(
				"192.0.2.11",
				retained.Records.Single(record => record.Type == ResourceType.A).Addresses.Single()
			);
			Assert.DoesNotContain(secondaryResolver.GetZones(), zone => zone.Suffix == "removed.example");
		}
		finally
		{
			await restartedCts.CancelAsync();
		}
	}

	[Fact]
	public async Task SecondarySync_StalledZoneDoesNotBlockOtherTransfers()
	{
		var port = GetAvailableTcpPort();
		using var transferControl = new StallingResolver(
			[
				CreateReplicationZone(1, "192.0.2.10", "stalled.example"),
				CreateReplicationZone(1, "192.0.2.20", "working.example"),
			]
		);
		var       primary    = CreateReplicationPrimary(port, transferControl);
		using var primaryCts = new CancellationTokenSource();
		await primary.Start(primaryCts.Token);

		var secondaryResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		using var secondary = CreateSecondaryProvider(
			secondaryResolver,
			port,
			settings =>
			{
				settings.MaxConcurrentTransfers    = 2;
				settings.TransferTimeoutSeconds    = 3;
				settings.TransferRetryDelaySeconds = 1;
			}
		);
		secondary.Start(CancellationToken.None);

		try
		{
			await transferControl.StalledTransferStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
			var workingZone = await WaitForNamedZoneAsync(
				secondaryResolver,
				"working.example",
				TimeSpan.FromSeconds(2)
			);
			Assert.Equal((uint)1, workingZone.Serial);
			Assert.DoesNotContain(secondaryResolver.GetZones(), zone => zone.Suffix == "stalled.example");

			var retriedZone = await WaitForNamedZoneAsync(
				secondaryResolver,
				"stalled.example",
				TimeSpan.FromSeconds(8)
			);
			Assert.Equal((uint)1, retriedZone.Serial);
			Assert.True(transferControl.StalledAttemptCount >= 2);
		}
		finally
		{
			transferControl.ReleaseStalledTransfer();
			await primaryCts.CancelAsync();
		}
	}

	[Fact]
	public async Task SecondarySync_FailedZoneRetriesUntilItSucceeds()
	{
		var       port             = GetAvailableTcpPort();
		var       retryingResolver = new RetryOnceResolver(CreateReplicationZone(7, "192.0.2.70", "retry.example"));
		var       primary          = CreateReplicationPrimary(port, retryingResolver);
		using var primaryCts       = new CancellationTokenSource();
		await primary.Start(primaryCts.Token);

		var secondaryResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		using var secondary = CreateSecondaryProvider(
			secondaryResolver,
			port,
			settings => settings.TransferRetryDelaySeconds = 1
		);
		secondary.Start(CancellationToken.None);

		try
		{
			var zone = await WaitForNamedZoneAsync(secondaryResolver, "retry.example", TimeSpan.FromSeconds(10));
			Assert.Equal((uint)7, zone.Serial);
			Assert.True(retryingResolver.AttemptCount >= 2);
		}
		finally
		{
			await primaryCts.CancelAsync();
		}
	}

	private static DnsServer CreateReplicationPrimary(ushort port, IDnsResolver resolver)
	{
		var options = Options.Create(
			new ServerOptions
			{
				DnsListener  = new DnsListenerOptions { Port     = port, TcpPort            = port },
				ZoneTransfer = new ZoneTransferOptions { Enabled = true, AllowTransfersFrom = ["127.0.0.1/32"], },
				WebServer    = new WebServerOptions(),
			}
		);
		var primary = new DnsServer(new FakeLogger<DnsServer>(), options);
		primary.Initialize([resolver]);
		return primary;
	}

	private static SecondaryZoneProvider CreateSecondaryProvider(
		IDnsResolver resolver,
		ushort port,
		Action<SecondarySyncOptions> configure = null
	)
	{
		var settings = new SecondarySyncOptions
		{
			Enabled = true, Master = $"127.0.0.1:{port}", ReconnectDelaySeconds = 1,
		};
		configure?.Invoke(settings);
		var options  = Options.Create(new ServerOptions { SecondarySync = settings, });
		var provider = new SecondaryZoneProvider(new FakeLogger<SecondaryZoneProvider>(), resolver, options);
		provider.Initialize(new ZoneOptions());
		return provider;
	}

	[Fact]
	public async Task SecondarySync_PublishesAndPersistsCurrentSnapshotImmediately()
	{
		var cacheFile = Path.Combine(Path.GetTempPath(), $"secondary-zones-{Guid.NewGuid():N}.json");
		try
		{
			var options = Options.Create(
				new ServerOptions
				{
					SecondarySync = new SecondarySyncOptions
					{
						Enabled = true, Master = "127.0.0.1:53", CacheFile = cacheFile,
					},
				}
			);
			var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
			using (var provider = new SecondaryZoneProvider(new FakeLogger<SecondaryZoneProvider>(), resolver, options))
			{
				provider.Initialize(new ZoneOptions());
				var zones = Assert.IsType<Dictionary<string, Zone>>(
					typeof(SecondaryZoneProvider).GetField("_zones", BindingFlags.Instance | BindingFlags.NonPublic)!
					                             .GetValue(provider)
				);
				zones["cached.example"] = CreateReplicationZone(9, "192.0.2.9", "cached.example");

				await InvokePrivate<Task>(provider, "PublishSnapshotAsync", CancellationToken.None);

				Assert.Contains(resolver.GetZones(), zone => zone.Suffix == "cached.example");
			}

			Assert.Contains("cached.example", await File.ReadAllTextAsync(cacheFile));

			var restoredResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
			using var restoredProvider = new SecondaryZoneProvider(
				new FakeLogger<SecondaryZoneProvider>(),
				restoredResolver,
				options
			);
			restoredProvider.Initialize(new ZoneOptions());
			await InvokePrivate<Task>(restoredProvider, "LoadCacheAsync", CancellationToken.None);

			var restoredZone = Assert.Single(restoredResolver.GetZones());
			Assert.Equal("cached.example", restoredZone.Suffix);
			Assert.Equal(4, restoredZone.Records.Count);
			Assert.Contains(restoredZone.Records, record => record.Type == ResourceType.A);
			Assert.Contains(restoredZone.Records, record => record.Type == ResourceType.TXT);
		}
		finally
		{
			File.Delete(cacheFile);
			File.Delete($"{cacheFile}.tmp");
		}
	}

	[Fact]
	public async Task SecondarySync_RetransfersIncompleteCachedZoneEvenWhenSerialMatches()
	{
		var port      = GetAvailableTcpPort();
		var cacheFile = Path.Combine(Path.GetTempPath(), $"secondary-zones-{Guid.NewGuid():N}.json");
		await File.WriteAllTextAsync(
			cacheFile,
			"""
			[
			  {
			    "Suffix": "repair.example",
			    "Serial": 15,
			    "Records": []
			  }
			]
			"""
		);

		var primaryResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		((IObserver<List<Zone>>)primaryResolver).OnNext([CreateReplicationZone(15, "192.0.2.15", "repair.example")]);
		var       primary    = CreateReplicationPrimary(port, primaryResolver);
		using var primaryCts = new CancellationTokenSource();
		await primary.Start(primaryCts.Token);

		var secondaryResolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		using var secondary = CreateSecondaryProvider(
			secondaryResolver,
			port,
			settings => settings.CacheFile = cacheFile
		);
		secondary.Start(CancellationToken.None);

		try
		{
			var repaired = await WaitForNamedZoneSerialAsync(
				secondaryResolver,
				"repair.example",
				15,
				TimeSpan.FromSeconds(10)
			);
			Assert.Equal(4, repaired.Records.Count);
			Assert.Contains(repaired.Records, record => record.Type == ResourceType.A);
			Assert.Contains(repaired.Records, record => record.Type == ResourceType.TXT);
		}
		finally
		{
			await primaryCts.CancelAsync();
			File.Delete(cacheFile);
			File.Delete($"{cacheFile}.tmp");
		}
	}

	private static Zone CreateReplicationZone(uint serial, string address, string suffix = "replicated.example")
	{
		var zone = new Zone { Suffix = suffix, Serial = serial };
		zone.Initialize(
			[
				new()
				{
					Host      = string.Empty,
					Type      = ResourceType.SOA,
					Class     = ResourceClass.IN,
					Addresses = [$"ns1.{suffix}", $"hostmaster.{suffix}"],
				},
				new()
				{
					Host      = string.Empty,
					Type      = ResourceType.NS,
					Class     = ResourceClass.IN,
					Addresses = [$"ns1.{suffix}"],
				},
				new()
				{
					Host = "www", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = [address],
				},
				new()
				{
					Host = "txt", Type = ResourceType.TXT, Class = ResourceClass.IN, Addresses = [new string('x', 200)],
				},
			]
		);
		return zone;
	}

	private static async Task<Zone> WaitForZoneSerialAsync(IDnsResolver resolver, uint serial)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		while (!timeout.IsCancellationRequested)
		{
			var zone = resolver.GetZones().SingleOrDefault(item => item.Suffix == "replicated.example");
			if (zone?.Serial == serial) return zone;
			await Task.Delay(25, timeout.Token);
		}

		throw new TimeoutException($"Secondary did not load serial {serial}.");
	}

	private static async Task WaitForZoneCountAsync(IDnsResolver resolver, int count)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		while (!timeout.IsCancellationRequested)
		{
			if (resolver.GetZones().Count() == count) return;
			await Task.Delay(25, timeout.Token);
		}

		throw new TimeoutException($"Secondary did not load {count} zones.");
	}

	private static async Task<Zone> WaitForNamedZoneAsync(IDnsResolver resolver, string suffix, TimeSpan timeout)
	{
		using var timeoutCts = new CancellationTokenSource(timeout);
		while (!timeoutCts.IsCancellationRequested)
		{
			var zone = resolver.GetZones()
			                   .SingleOrDefault(item => string.Equals(
				                                    item.Suffix,
				                                    suffix,
				                                    StringComparison.OrdinalIgnoreCase
			                                    )
			                   );
			if (zone != null) return zone;
			await Task.Delay(25, timeoutCts.Token);
		}

		throw new TimeoutException($"Secondary did not load {suffix}.");
	}

	private static async Task<Zone> WaitForNamedZoneSerialAsync(
		IDnsResolver resolver,
		string suffix,
		uint serial,
		TimeSpan timeout
	)
	{
		using var timeoutCts = new CancellationTokenSource(timeout);
		while (!timeoutCts.IsCancellationRequested)
		{
			var zone = resolver.GetZones()
			                   .SingleOrDefault(item => string.Equals(
				                                            item.Suffix,
				                                            suffix,
				                                            StringComparison.OrdinalIgnoreCase
			                                            ) &&
			                                            item.Serial == serial
			                   );
			if (zone != null) return zone;
			await Task.Delay(25, timeoutCts.Token);
		}

		throw new TimeoutException($"Secondary did not load {suffix} at serial {serial}.");
	}

	private static async Task<string> WaitForFileContainingAsync(string path, string expectedContent, TimeSpan timeout)
	{
		using var timeoutCts = new CancellationTokenSource(timeout);
		while (!timeoutCts.IsCancellationRequested)
		{
			if (File.Exists(path))
			{
				var content = await File.ReadAllTextAsync(path, timeoutCts.Token);
				if (content.Contains(expectedContent, StringComparison.Ordinal)) return content;
			}

			await Task.Delay(25, timeoutCts.Token);
		}

		throw new TimeoutException($"File '{path}' did not contain '{expectedContent}'.");
	}

	private static ushort GetAvailableTcpPort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	private static void AssertCatalogSnapshot(byte[] payload, params uint[] expectedSerials)
	{
		Assert.True(DnsMessage.TryParse(payload, out var response));
		Assert.Equal((byte)RCode.NOERROR, response.RCode);
		Assert.True(response.AA);
		var soaRecords = response.Answers.Where(record => record.Type == ResourceType.SOA).ToList();
		Assert.Equal(expectedSerials.Length + 2, soaRecords.Count);
		Assert.Equal(DnsServer.CatalogZoneName, soaRecords.First().Name);
		Assert.Equal(DnsServer.CatalogZoneName, soaRecords.Last().Name);
		Assert.Equal(
			expectedSerials,
			soaRecords.Skip(1).SkipLast(1).Select(record => Assert.IsType<SOARData>(record.RData).Serial)
		);
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
		var server = CreateServer(
			zoneTransferEnabled: true,
			allowTransfersFrom: ["127.0.0.1/32"],
			resolvers: [new FakeResolver([])]
		);

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
		var server      = CreateServer();
		var udpListener = new UdpListener();
		udpListener.Initialize(0);
		SetPrivateField(server, "_udpListener", udpListener);

		var zone = new Zone { Suffix = "example.com", Serial = 2 };
		InvokePrivateVoid(server, "SendNotify", zone, "example.com", new IPEndPoint(IPAddress.Loopback, 5302));

		var response = new DnsMessage { QueryIdentifier = 0xAAAA, QuestionCount = 1 };
		response.Questions.Add(new Question("example.com", ResourceType.A, ResourceClass.IN));
		InvokePrivateVoid(server, "SendUdpResponse", response, new IPEndPoint(IPAddress.Loopback, 5302));
	}

	[Fact]
	public async Task SendUdpAsync_DoesNotPropagateSocketShutdownFailure()
	{
		var server      = CreateServer();
		var udpListener = new UdpListener();
		udpListener.Initialize(0);
		udpListener.Start();
		udpListener.Stop();
		SetPrivateField(server, "_udpListener", udpListener);

		await InvokePrivate<Task>(
			server,
			"SendUdpAsync",
			new byte[] { 0x53 },
			0,
			1,
			new IPEndPoint(IPAddress.Loopback, 53)
		);
	}

	[Fact]
	public void ProcessUdpRequest_CoversNotifyAxfrAndQueryBranches()
	{
		var zone = new Zone { Suffix = "example.com", Serial = 9 };
		zone.Initialize(
			[
				new ZoneRecord
				{
					Host = "www", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.11"]
				},
				new ZoneRecord
				{
					Host = "www", Type = ResourceType.CNAME, Class = ResourceClass.IN, Addresses = ["external.invalid"]
				},
			]
		);

		var server = CreateServer(
			resolvers: [new FakeResolver([zone])],
			zoneTransferEnabled: true,
			allowTransfersFrom: ["127.0.0.1/32"]
		);
		var udpListener = new UdpListener();
		udpListener.Initialize(0);
		SetPrivateField(server, "_udpListener", udpListener);
		SetPrivateField(server, "_defaultDns", Array.Empty<IPAddress>());

		var remote = new IPEndPoint(IPAddress.Loopback, 5310);

		var notify = new DnsMessage { QueryIdentifier = 0x1001, Opcode = (byte)OpCode.NOTIFY, QuestionCount = 1, };
		notify.Questions.Add(new Question("www.example.com", ResourceType.SOA, ResourceClass.IN));
		var notifyBytes = Serialize(notify);
		InvokePrivateVoid(server, "ProcessUdpRequest", notifyBytes, notifyBytes.Length, remote);

		var udpAxfr = new DnsMessage { QueryIdentifier = 0x1002, QuestionCount = 1, };
		udpAxfr.Questions.Add(new Question("example.com", ResourceType.AXFR, ResourceClass.IN));
		var udpAxfrBytes = Serialize(udpAxfr);
		InvokePrivateVoid(server, "ProcessUdpRequest", udpAxfrBytes, udpAxfrBytes.Length, remote);

		var ptr = new DnsMessage { QueryIdentifier = 0x1003, QuestionCount = 1, };
		ptr.Questions.Add(new Question("1.0.0.127.in-addr.arpa", ResourceType.PTR, ResourceClass.IN));
		var ptrBytes = Serialize(ptr);
		InvokePrivateVoid(server, "ProcessUdpRequest", ptrBytes, ptrBytes.Length, remote);

		var queryExisting = new DnsMessage { QueryIdentifier = 0x1004, QuestionCount = 1, };
		queryExisting.Questions.Add(new Question("www.example.com", ResourceType.A, ResourceClass.IN));
		var queryExistingBytes = Serialize(queryExisting);
		InvokePrivateVoid(server, "ProcessUdpRequest", queryExistingBytes, queryExistingBytes.Length, remote);

		var queryMissingName = new DnsMessage { QueryIdentifier = 0x1005, QuestionCount = 1, };
		queryMissingName.Questions.Add(new Question("missing.example.com", ResourceType.A, ResourceClass.IN));
		var queryMissingBytes = Serialize(queryMissingName);
		InvokePrivateVoid(server, "ProcessUdpRequest", queryMissingBytes, queryMissingBytes.Length, remote);

		var queryMissingType = new DnsMessage { QueryIdentifier = 0x1006, QuestionCount = 1, };
		queryMissingType.Questions.Add(new Question("www.example.com", ResourceType.TXT, ResourceClass.IN));
		var queryMissingTypeBytes = Serialize(queryMissingType);
		InvokePrivateVoid(server, "ProcessUdpRequest", queryMissingTypeBytes, queryMissingTypeBytes.Length, remote);

		var unresolvedQuery = new DnsMessage { QueryIdentifier = 0x1007, QuestionCount = 1, };
		unresolvedQuery.Questions.Add(new Question("outside.invalid", ResourceType.A, ResourceClass.IN));
		var unresolvedBytes = Serialize(unresolvedQuery);
		InvokePrivateVoid(server, "ProcessUdpRequest", unresolvedBytes, unresolvedBytes.Length, remote);

		var upstreamResponse = new DnsMessage
		{
			QueryIdentifier = 0x1007, QR = true, QuestionCount = 1, AnswerCount = 1,
		};
		upstreamResponse.Questions.Add(new Question("outside.invalid", ResourceType.A, ResourceClass.IN));
		upstreamResponse.Answers.Add(
			new ResourceRecord
			{
				Name  = "outside.invalid",
				Class = ResourceClass.IN,
				Type  = ResourceType.A,
				TTL   = 30,
				RData = new ANameRData { Address = IPAddress.Parse("203.0.113.1") },
			}
		);
		var upstreamResponseBytes = Serialize(upstreamResponse);
		InvokePrivateVoid(server, "ProcessUdpRequest", upstreamResponseBytes, upstreamResponseBytes.Length, remote);
	}

	[Fact]
	public void HandleRecords_CoversAllSupportedTypesWithoutRecursiveCnamePath()
	{
		var server  = CreateServer();
		var message = new DnsMessage();
		var zone    = new Zone { Suffix = "example.com", Serial = 5 };

		var zoneRecords = new List<ZoneRecord>
		{
			new()
			{
				Host = "ns", Type = ResourceType.NS, Class = ResourceClass.IN, Addresses = ["ns1.example.com"]
			},
			new()
			{
				Host      = "mx",
				Type      = ResourceType.MX,
				Class     = ResourceClass.IN,
				Addresses = ["10 mail.example.com"]
			},
			new() { Host = "a", Type = ResourceType.A, Class = ResourceClass.IN, Addresses = ["192.0.2.1"] },
			new()
			{
				Host      = "alias",
				Type      = ResourceType.CNAME,
				Class     = ResourceClass.IN,
				Addresses = ["external.invalid"]
			},
			new()
			{
				Host      = "soa",
				Type      = ResourceType.SOA,
				Class     = ResourceClass.IN,
				Addresses = ["ns1.example.com", "hostmaster.example.com"]
			},
			new() { Host = "txt", Type = ResourceType.TXT, Class = ResourceClass.IN, Addresses = ["v=spf1 -all"] },
			new()
			{
				Host      = "ptr",
				Type      = ResourceType.PTR,
				Class     = ResourceClass.IN,
				Addresses = ["host.example.com"]
			},
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

	[Fact]
	public void HandleRecords_CnameAtShorthandTargetResolvesToZoneApex()
	{
		var server  = CreateServer();
		var message = new DnsMessage();
		var zone    = new Zone { Suffix = "example.com", Serial = 5 };
		var zoneRecords = new List<ZoneRecord>
		{
			new() { Host = "www", Type = ResourceType.CNAME, Class = ResourceClass.IN, Addresses = ["@."] },
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

		var cname = Assert.Single(message.Answers, answer => answer.Type == ResourceType.CNAME);
		Assert.Equal("example.com", Assert.IsType<CNameRData>(cname.RData).Name);
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
				Enabled            = zoneTransferEnabled,
				AllowTransfersFrom = allowTransfersFrom ?? ["127.0.0.1/32"],
				NotifySecondaries  = [],
				InjectedNsAddress  = injectedNsAddress,
			},
			WebServer = new WebServerOptions(),
		};

		var server = new DnsServer(new FakeLogger<DnsServer>(), Options.Create(options));

		typeof(DnsServer).GetField("_resolvers", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(
			server,
			resolvers ?? []
		);

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

	private static T InvokePrivateStatic<T>(Type type, string methodName, params object[] args)
	{
		var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
		return (T)method.Invoke(null, args)!;
	}

	private static void SetPrivateField(object instance, string fieldName, object value)
	{
		instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(
			instance,
			value
		);
	}

	private sealed class FakeResolver(List<Zone> zones) : IDnsResolver
	{
		private readonly List<Zone> _zones = zones;

		event EventHandler IDnsResolver.ZonesChanged
		{
			add { }
			remove { }
		}

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

	private sealed class StallingResolver(List<Zone> zones) : IDnsResolver, IDisposable
	{
		private readonly ManualResetEventSlim _releaseStalledTransfer = new(false);
		private          int                  _stalledAttemptCount;

		public int StalledAttemptCount => Volatile.Read(ref _stalledAttemptCount);

		public TaskCompletionSource<bool> StalledTransferStarted { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		event EventHandler IDnsResolver.ZonesChanged
		{
			add { }
			remove { }
		}

		public bool TryGetZone(string hostname, out Zone zone)
		{
			if (hostname.EndsWith("stalled.example", StringComparison.OrdinalIgnoreCase) &&
			    Interlocked.Increment(ref _stalledAttemptCount) == 1)
			{
				StalledTransferStarted.TrySetResult(true);
				_releaseStalledTransfer.Wait(TimeSpan.FromSeconds(15));
			}

			zone = zones.FirstOrDefault(item => hostname.EndsWith(item.Suffix, StringComparison.OrdinalIgnoreCase));
			return zone != null;
		}

		public IEnumerable<Zone> GetZones() => zones;

		public void ReleaseStalledTransfer() => _releaseStalledTransfer.Set();

		public void Dispose() => _releaseStalledTransfer.Dispose();

		public void SubscribeTo(IObservable<List<Zone>> zoneProvider)
		{
		}

		public void DumpHtml(TextWriter writer)
		{
		}

		public object GetObject() => zones;

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

	private sealed class RetryOnceResolver(Zone zone) : IDnsResolver
	{
		private int _attemptCount;

		public int AttemptCount => Volatile.Read(ref _attemptCount);

		event EventHandler IDnsResolver.ZonesChanged
		{
			add { }
			remove { }
		}

		public bool TryGetZone(string hostname, out Zone result)
		{
			if (Interlocked.Increment(ref _attemptCount) == 1)
			{
				result = null;
				return false;
			}

			result = zone;
			return true;
		}

		public IEnumerable<Zone> GetZones() => [zone];

		public void SubscribeTo(IObservable<List<Zone>> zoneProvider)
		{
		}

		public void DumpHtml(TextWriter writer)
		{
		}

		public object GetObject() => zone;

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