// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="DnsServer.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Models;
using Dns.Models.Dns.Packets;
using Dns.Models.Enums;
using Dns.RDataTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dns;

public class DnsServer(ILogger<DnsServer> logger, IOptions<ServerOptions> serverOptions) : IDnsServer
{
	public const            string   CatalogZoneName          = "_dns-zone-catalog";
	private static readonly TimeSpan CatalogHeartbeatInterval = TimeSpan.FromSeconds(30);

	private readonly ConcurrentDictionary<Guid, Channel<bool>> _catalogSubscribers = new();

	/// <summary>
	///     Maps forwarded DNS requests to their originating endpoints.
	/// </summary>
	private readonly ConcurrentDictionary<DnsRequestKey, EndPoint> _requestResponseMap = new();

	private readonly ConcurrentDictionary<string, HostAddressCacheEntry> _hostAddressCache =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly        Dictionary<string, uint> _zoneSerials = new(StringComparer.OrdinalIgnoreCase);
	private static readonly TimeSpan                 TransferHostAddressCacheDuration = TimeSpan.FromMinutes(5);

	private IPAddress[]               _defaultDns;
	private Func<string, IPAddress[]> _hostAddressResolver = System.Net.Dns.GetHostAddresses;
	private long                      _nacks;
	private CancellationToken         _notifyLoopCancellationToken;
	private List<string>              _notifyTargetEntries = [];
	private List<IPEndPoint>          _notifyTargets       = [];
	private long                      _requests;
	private List<IDnsResolver>        _resolvers; // resolver for name entries
	private long                      _responses;
	private int                       _catalogSerial = 1;
	private TcpDnsListener            _tcpListener;
	private UdpListener               _udpListener; // listener for UDP53 traffic

	/// <summary>Initialize server with specified domain name resolver</summary>
	/// <param name="resolvers"></param>
	public void Initialize(List<IDnsResolver> resolvers)
	{
		if (_resolvers != null)
			foreach (var resolver in _resolvers)
				resolver.ZonesChanged -= OnResolverZonesChanged;

		_resolvers = resolvers;
		foreach (var resolver in _resolvers)
			resolver.ZonesChanged += OnResolverZonesChanged;

		_udpListener = new();
		_tcpListener = new();

		_udpListener.Initialize(serverOptions.Value.DnsListener.Port);
		_udpListener.OnRequest += ProcessUdpRequest;
		var tcpPort = serverOptions.Value.DnsListener.TcpPort ?? serverOptions.Value.DnsListener.Port;
		_tcpListener.Initialize(tcpPort);
		_tcpListener.OnRequest       += ProcessTcpRequest;
		_tcpListener.OnStreamRequest += ProcessTcpStreamRequest;

		_defaultDns          = GetDefaultDNS().ToArray();
		_notifyTargetEntries = serverOptions.Value.ZoneTransfer.NotifySecondaries?.ToList() ?? [];
		_notifyTargets       = ParseNotifyTargets(_notifyTargetEntries);
	}

	public Task Start(CancellationToken ct)
	{
		_udpListener.Start();
		_tcpListener.Start();
		ct.Register(_udpListener.Stop);
		ct.Register(_tcpListener.Stop);

		if (serverOptions.Value.ZoneTransfer.Enabled && _notifyTargetEntries.Count > 0)
		{
			_notifyLoopCancellationToken = ct;
			_                            = Task.Run(RunNotifyLoop, ct);
		}

		return Task.CompletedTask;
	}

	public void DumpHtml(TextWriter writer)
	{
		writer.WriteLine("DNS Server Status<br/>");
		writer.Write("Default Nameservers:");
		foreach (var dns in _defaultDns) writer.WriteLine(dns);

		writer.WriteLine("DNS Server Status<br/>");
	}

	public object GetObject() => _defaultDns;

	private Task<byte[]> ProcessTcpRequest(byte[] buffer, int length, EndPoint remoteEndPoint)
	{
		if (!DnsProtocol.TryParse(buffer, length, out var message))
		{
			logger.LogError("unable to parse tcp message");
			return Task.FromResult<byte[]>(null);
		}

		Interlocked.Increment(ref _requests);

		if (message.IsQuery())
		{
			var response = BuildResponseForQuery(message, remoteEndPoint, true);
			return response == null ? Task.FromResult<byte[]>(null) : Task.FromResult(SerializeMessage(response));
		}

		Interlocked.Increment(ref _nacks);
		return Task.FromResult<byte[]>(null);
	}

	private IAsyncEnumerable<byte[]> ProcessTcpStreamRequest(
		byte[] buffer,
		int length,
		EndPoint remoteEndPoint,
		CancellationToken cancellationToken
	)
	{
		if (!DnsProtocol.TryParse(buffer, length, out var message) || message.Questions.Count == 0)
			return null;

		var question = message.Questions[0];
		if (message.Opcode != (byte)OpCode.QUERY ||
		    question.Type != ResourceType.AXFR ||
		    !string.Equals(CanonicalZoneName(question.Name), CatalogZoneName, StringComparison.OrdinalIgnoreCase))
			return null;

		if (!serverOptions.Value.ZoneTransfer.Enabled || !IsTransferAllowed(remoteEndPoint))
			return SingleTcpResponse(SerializeMessage(BuildBasicResponse(message, (byte)RCode.REFUSED, true, false)));

		return StreamCatalog(message, cancellationToken);
	}

	private static async IAsyncEnumerable<byte[]> SingleTcpResponse(byte[] response)
	{
		yield return response;
		await Task.CompletedTask.ConfigureAwait(false);
	}

	private async IAsyncEnumerable<byte[]> StreamCatalog(
		DnsMessage request,
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		await WaitForCatalogReadinessAsync(cancellationToken).ConfigureAwait(false);

		var subscriptionId = Guid.NewGuid();
		var updates = Channel.CreateBounded<bool>(
			new BoundedChannelOptions(1)
			{
				FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false,
			}
		);
		_catalogSubscribers[subscriptionId] = updates;

		try
		{
			yield return SerializeMessage(BuildCatalogResponse(request));

			while (!cancellationToken.IsCancellationRequested)
			{
				using var iterationCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				var       updateTask    = updates.Reader.ReadAsync(iterationCts.Token).AsTask();
				var       heartbeatTask = Task.Delay(CatalogHeartbeatInterval, iterationCts.Token);
				await Task.WhenAny(updateTask, heartbeatTask).ConfigureAwait(false);
				await iterationCts.CancelAsync().ConfigureAwait(false);

				while (updates.Reader.TryRead(out _))
				{
				}

				yield return SerializeMessage(BuildCatalogResponse(request));
			}
		}
		finally
		{
			if (_catalogSubscribers.TryRemove(subscriptionId, out var removed))
				removed.Writer.TryComplete();
		}
	}

	private async Task WaitForCatalogReadinessAsync(CancellationToken cancellationToken)
	{
		var pendingResolvers = (_resolvers ?? []).Where(resolver => !resolver.IsReady).ToList();
		if (pendingResolvers.Count == 0) return;

		logger.LogInformation(
			"Holding primary catalog stream until {PendingResolverCount} resolver(s) finish their initial load",
			pendingResolvers.Count
		);
		await Task.WhenAll(pendingResolvers.Select(resolver => resolver.WaitUntilReadyAsync(cancellationToken)))
		          .ConfigureAwait(false);
		logger.LogInformation("Primary catalog is ready after all resolvers completed their initial load");
	}

	private DnsMessage BuildCatalogResponse(DnsMessage request)
	{
		var response = BuildBasicResponse(request, (byte)RCode.NOERROR, true, false);
		var catalogZone = new Zone
		{
			Suffix = CatalogZoneName, Serial = unchecked((uint)Volatile.Read(ref _catalogSerial))
		};
		response.Answers.Add(CreateSoaRecord(CatalogZoneName, catalogZone));

		var zones = (_resolvers ?? []).SelectMany(resolver => resolver.GetZones())
		                              .Where(zone => zone != null && !string.IsNullOrWhiteSpace(zone.Suffix))
		                              .GroupBy(zone => CanonicalZoneName(zone.Suffix), StringComparer.OrdinalIgnoreCase)
		                              .Select(group => group.First())
		                              .OrderBy(zone => zone.Suffix, StringComparer.OrdinalIgnoreCase);

		foreach (var zone in zones)
			response.Answers.Add(CreateSoaRecord(CanonicalZoneName(zone.Suffix), zone));

		response.Answers.Add(CreateSoaRecord(CatalogZoneName, catalogZone));
		response.AnswerCount = (ushort)response.Answers.Count;
		return response;
	}

	private void OnResolverZonesChanged(object sender, EventArgs args)
	{
		Interlocked.Increment(ref _catalogSerial);
		foreach (var subscriber in _catalogSubscribers.Values)
			subscriber.Writer.TryWrite(true);
	}

	/// <summary>Process UDP Request</summary>
	/// <param name="buffer">The received data buffer.</param>
	/// <param name="length">The number of valid bytes in the buffer.</param>
	/// <param name="remoteEndPoint">The remote endpoint that sent the request.</param>
	private void ProcessUdpRequest(byte[] buffer, int length, EndPoint remoteEndPoint)
	{
		if (!DnsProtocol.TryParse(buffer, length, out var message))
		{
			// TODO log bad message
			logger.LogError("unable to parse message");
			return;
		}

		Interlocked.Increment(ref _requests);

		if (message.IsQuery() && message.Opcode == (byte)OpCode.NOTIFY)
		{
			var notifyResponse = BuildNotifyResponse(message, remoteEndPoint);
			SendUdpResponse(notifyResponse, remoteEndPoint);
			return;
		}

		if (message.IsQuery() &&
		    message.Questions.Count > 0 &&
		    message.Questions[0].Type is ResourceType.AXFR or ResourceType.IXFR)
		{
			var refused = BuildBasicResponse(
				message,
				(byte)RCode.REFUSED,
				authoritative: true,
				recursionAvailable: false
			);
			SendUdpResponse(refused, remoteEndPoint);
			return;
		}

		if (message.IsQuery())
		{
			if (message.Questions.Count == 0)
			{
				SendUdpResponse(
					BuildBasicResponse(message, (byte)RCode.FORMERR, authoritative: false, recursionAvailable: false),
					remoteEndPoint
				);
				return;
			}

			var recursionAllowed      = IsRecursionAllowed(remoteEndPoint);
			var authoritativeResponse = BuildAuthoritativeResponse(message, remoteEndPoint, recursionAllowed);
			if (authoritativeResponse != null)
			{
				SendUdpResponse(authoritativeResponse, remoteEndPoint);
				return;
			}

			if (!recursionAllowed || !message.RD)
			{
				SendUdpResponse(
					BuildBasicResponse(
						message,
						(byte)RCode.REFUSED,
						authoritative: false,
						recursionAvailable: recursionAllowed
					),
					remoteEndPoint
				);
				return;
			}

			// Store the client endpoint before forwarding the original query to the upstream resolvers.
			var key = new DnsRequestKey(message);
			_requestResponseMap.TryAdd(key, remoteEndPoint);
			var payload = SerializeMessage(message);
			foreach (var dnsServer in _defaultDns)
				SendUdp(payload, 0, payload.Length, new IPEndPoint(dnsServer, 53));
		}
		else
		{
			// message is response to a delegated query
			var key = new DnsRequestKey(message);

			if (_requestResponseMap.TryRemove(key, out var ep))
				using (var responseStream = BufferPool.RentMemoryStream())
				{
					message.WriteToStream(responseStream);
					Interlocked.Increment(ref _responses);

					logger.LogInformation(
						"{@RemoteEndPoint} answered {Name} {Class} {Type} to {@EndPoint}",
						remoteEndPoint,
						message.Questions[0].Name,
						message.Questions[0].Class,
						message.Questions[0].Type,
						ep
					);

					SendUdp(responseStream.GetBuffer(), 0, (int)responseStream.Position, ep);
				}
			else
				Interlocked.Increment(ref _nacks);
		}
	}

	private DnsMessage BuildResponseForQuery(DnsMessage message, EndPoint remoteEndPoint, bool viaTcp)
	{
		if (message.Questions.Count == 0)
			return BuildBasicResponse(message, (byte)RCode.FORMERR, authoritative: false, recursionAvailable: false);

		if (message.Opcode == (byte)OpCode.NOTIFY) return BuildNotifyResponse(message, remoteEndPoint);

		if (message.Opcode != (byte)OpCode.QUERY)
			return BuildBasicResponse(message, (byte)RCode.NOTIMP, authoritative: false, recursionAvailable: false);

		var question = message.Questions[0];
		if (question.Type is ResourceType.AXFR or ResourceType.IXFR)
			return BuildTransferResponse(message, question, remoteEndPoint, viaTcp);

		return BuildAuthoritativeResponse(
			       message,
			       remoteEndPoint,
			       recursionAvailable: !viaTcp && IsRecursionAllowed(remoteEndPoint)
		       ) ??
		       BuildBasicResponse(message, (byte)RCode.REFUSED, authoritative: false, recursionAvailable: false);
	}

	private DnsMessage BuildAuthoritativeResponse(DnsMessage request, EndPoint remoteEndPoint, bool recursionAvailable)
	{
		var question = request.Questions[0];
		logger.LogInformation(
			"{@RemoteEndPoint} asked for {Name} {Class} {Type}",
			remoteEndPoint,
			question.Name,
			question.Class,
			question.Type
		);

		var response = BuildBasicResponse(request, (byte)RCode.NOERROR, authoritative: true, recursionAvailable);

		if (question.Type == ResourceType.PTR && question.Name == "1.0.0.127.in-addr.arpa")
		{
			response.Answers.Add(
				new()
				{
					Name       = question.Name,
					Class      = ResourceClass.IN,
					Type       = ResourceType.PTR,
					TTL        = 3600,
					DataLength = 0xB,
					RData      = new DomainNamePointRData { Name = "localhost" },
				}
			);
			response.AnswerCount = 1;
			return response;
		}

		if (!TryResolveZone(question.Name, out var zone)) return null;

		var zoneName    = CanonicalZoneName(zone.Suffix);
		var qName       = GetRelativeHostName(question.Name, zoneName);
		var zoneRecords = FindZoneRecords(zone, qName, question.Type);
		if (zoneRecords.Count > 0)
		{
			HandleRecords(zoneRecords, question, response, zone);
			return response;
		}

		var zoneSoa = zone.Records.FirstOrDefault(record => record.Type == ResourceType.SOA);
		var isZoneApexQuery = string.Equals(
			question.Name.Trim().TrimEnd('.'),
			zoneName,
			StringComparison.OrdinalIgnoreCase
		);
		if (question.Type == ResourceType.SOA && isZoneApexQuery)
		{
			response.Answers.Add(CreateSoaRecord(zoneName, zone, zoneSoa));
			response.AnswerCount = 1;
			return response;
		}

		var nameExists = isZoneApexQuery ||
		                 zone.Records.Any(record => string.Equals(
			                                  record.Host,
			                                  qName,
			                                  StringComparison.OrdinalIgnoreCase
		                                  )
		                 );
		response.RCode = (byte)(nameExists ? RCode.NOERROR : RCode.NXDOMAIN);
		response.Authorities.Add(CreateSoaRecord(zoneName, zone, zoneSoa));
		response.NameServerCount = 1;
		return response;
	}

	private static List<ZoneRecord> FindZoneRecords(Zone zone, string relativeName, ResourceType resourceType) =>
		resourceType switch
		{
			ResourceType.ANY => zone.Records.Where(record => string.Equals(
				                                       record.Host,
				                                       relativeName,
				                                       StringComparison.OrdinalIgnoreCase
			                                       )
			                        )
			                        .ToList(),
			ResourceType.A => zone.Records.Where(record => record.Type is ResourceType.A or ResourceType.CNAME &&
			                                               string.Equals(
				                                               record.Host,
				                                               relativeName,
				                                               StringComparison.OrdinalIgnoreCase
			                                               )
			                      )
			                      .ToList(),
			ResourceType.AAAA => zone.Records.Where(record => record.Type is ResourceType.AAAA or ResourceType.CNAME &&
			                                                  string.Equals(
				                                                  record.Host,
				                                                  relativeName,
				                                                  StringComparison.OrdinalIgnoreCase
			                                                  )
			                         )
			                         .ToList(),
			_ => zone.Records.Where(record => record.Type == resourceType &&
			                                  string.Equals(
				                                  record.Host,
				                                  relativeName,
				                                  StringComparison.OrdinalIgnoreCase
			                                  )
			         )
			         .ToList(),
		};

	private static string GetRelativeHostName(string hostName, string zoneName)
	{
		var canonicalHost = CanonicalZoneName(hostName);
		if (string.Equals(canonicalHost, zoneName, StringComparison.OrdinalIgnoreCase)) return string.Empty;

		var zoneSuffix = $".{zoneName}";
		return canonicalHost.EndsWith(zoneSuffix, StringComparison.OrdinalIgnoreCase)
			? canonicalHost[..^zoneSuffix.Length]
			: canonicalHost;
	}

	private DnsMessage BuildNotifyResponse(DnsMessage message, EndPoint remoteEndPoint)
	{
		if (message.Questions.Count == 0)
			return BuildBasicResponse(message, (byte)RCode.FORMERR, authoritative: false, recursionAvailable: false);

		var zoneExists = _resolvers.Any(resolver => resolver.TryGetZone(message.Questions[0].Name, out _));
		return BuildBasicResponse(
			message,
			zoneExists ? (byte)RCode.NOERROR : (byte)RCode.NOTAUTH,
			authoritative: zoneExists,
			recursionAvailable: false
		);
	}

	private DnsMessage BuildTransferResponse(
		DnsMessage message,
		Question question,
		EndPoint remoteEndPoint,
		bool viaTcp
	)
	{
		if (!serverOptions.Value.ZoneTransfer.Enabled || !viaTcp || !IsTransferAllowed(remoteEndPoint))
			return BuildBasicResponse(message, (byte)RCode.REFUSED, authoritative: true, recursionAvailable: false);

		if (!TryResolveZone(question.Name, out var transferZone))
			return BuildBasicResponse(message, (byte)RCode.NOTAUTH, authoritative: false, recursionAvailable: false);

		var response = BuildBasicResponse(message, (byte)RCode.NOERROR, authoritative: true, recursionAvailable: false);
		var zoneName = CanonicalZoneName(transferZone.Suffix);
		var records = question.Type == ResourceType.IXFR
			? BuildIxfrRecords(message, transferZone, zoneName)
			: BuildAxfrRecords(transferZone, zoneName);

		foreach (var record in records)
		{
			response.Answers.Add(record);
			response.AnswerCount++;
		}

		return response;
	}

	private List<ResourceRecord> BuildIxfrRecords(DnsMessage message, Zone zone, string zoneName)
	{
		var clientSerial = message.Authorities.Where(authority => authority.Type == ResourceType.SOA)
		                          .Select(authority => authority.RData)
		                          .OfType<SOARData>()
		                          .Select(soa => (uint?)soa.Serial)
		                          .FirstOrDefault();

		if (clientSerial.HasValue && clientSerial.Value >= zone.Serial) return [CreateSoaRecord(zoneName, zone)];

		return BuildAxfrRecords(zone, zoneName);
	}

	private List<ResourceRecord> BuildAxfrRecords(Zone zone, string zoneName)
	{
		var zoneSoa =
			zone.Records.FirstOrDefault(record => record.Type == ResourceType.SOA &&
			                                      string.IsNullOrWhiteSpace(record.Host)
			);
		var answers = new List<ResourceRecord> { CreateSoaRecord(zoneName, zone, zoneSoa) };

		foreach (var zoneRecord in zone.Records)
		{
			foreach (var rr in BuildResourceRecords(zoneRecord, zone, zoneName))
			{
				if (rr.Type == ResourceType.SOA) continue;
				answers.Add(rr);
			}
		}

		if (!answers.Any(record => record.Type == ResourceType.NS &&
		                           string.Equals(record.Name, zoneName, StringComparison.OrdinalIgnoreCase)
		    ))
		{
			var nsRecord = CreateNsRecord(zoneName, zone);
			answers.Add(nsRecord);
			var nsAddressRecord = CreateInjectedNsAddressRecord(nsRecord, zoneName);
			if (nsAddressRecord != null) answers.Add(nsAddressRecord);
		}

		answers.Add(CreateSoaRecord(zoneName, zone, zoneSoa));
		return answers;
	}

	private List<ResourceRecord> BuildResourceRecords(ZoneRecord zoneRecord, Zone zone, string zoneName)
	{
		var name    = BuildRecordOwnerName(zoneName, zoneRecord.Host);
		var records = new List<ResourceRecord>();

		switch (zoneRecord.Type)
		{
			case ResourceType.NS:
				records.AddRange(
					zoneRecord.Addresses.Select(address => new ResourceRecord
						{
							Name  = name,
							Class = zoneRecord.Class,
							Type  = zoneRecord.Type,
							TTL   = 10,
							RData = new NSRData { Name = address },
						}
					)
				);
				break;
			case ResourceType.MX:
				records.AddRange(
					zoneRecord.Addresses.Select(address =>
						{
							var addressSplit = address.Split(' ');
							return new ResourceRecord
							{
								Name  = name,
								Class = zoneRecord.Class,
								Type  = zoneRecord.Type,
								TTL   = 10,
								RData = new MXRData
								{
									Name = addressSplit[1], Preference = Convert.ToUInt16(addressSplit[0])
								},
							};
						}
					)
				);
				break;
			case ResourceType.A:
			case ResourceType.AAAA:
				records.AddRange(
					zoneRecord.Addresses.Select(address => new ResourceRecord
						{
							Name  = name,
							Class = zoneRecord.Class,
							Type  = zoneRecord.Type,
							TTL   = 10,
							RData = new ANameRData { Address = IPAddress.Parse(address) },
						}
					)
				);
				break;
			case ResourceType.CNAME:
				records.AddRange(
					zoneRecord.Addresses.Select(address => new ResourceRecord
						{
							Name  = name,
							Class = zoneRecord.Class,
							Type  = zoneRecord.Type,
							TTL   = 10,
							RData = new CNameRData
							{
								Name = NormalizeAliasTarget(address, zoneName)
							},
						}
					)
				);
				break;
			case ResourceType.TXT:
				records.AddRange(
					zoneRecord.Addresses.Select(address => new ResourceRecord
						{
							Name  = name,
							Class = zoneRecord.Class,
							Type  = zoneRecord.Type,
							TTL   = 10,
							RData = new TXTRData { Name = address },
						}
					)
				);
				break;
			case ResourceType.PTR:
				records.AddRange(
					zoneRecord.Addresses.Select(address => new ResourceRecord
						{
							Name  = name,
							Class = zoneRecord.Class,
							Type  = zoneRecord.Type,
							TTL   = 10,
							RData = new DomainNamePointRData { Name = address },
						}
					)
				);
				break;
			case ResourceType.SOA:
				records.Add(CreateSoaRecord(name, zone, zoneRecord));
				break;
		}

		return records;
	}

	private static string BuildRecordOwnerName(string zoneName, string host)
	{
		if (string.IsNullOrWhiteSpace(host)) return zoneName;

		var normalizedHost = host.Trim().TrimEnd('.');
		if (normalizedHost.EndsWith(zoneName, StringComparison.OrdinalIgnoreCase)) return normalizedHost;

		return $"{normalizedHost}.{zoneName}";
	}

	private static string NormalizeAliasTarget(string address, string zoneName)
	{
		if (string.IsNullOrWhiteSpace(address)) return address;

		var normalized = address.Trim();
		if (normalized is "@" or "@." or "\\@" or "\\@.")
			return zoneName;

		return normalized;
	}

	private static string CanonicalZoneName(string suffix) => suffix?.Trim().Trim('.') ?? string.Empty;

	private sealed record SoaFields(
		string PrimaryNameServer,
		string ResponsibleMailbox,
		uint Refresh,
		uint Retry,
		uint Expire,
		uint MinimumTtl
	);

	private static byte[] SerializeMessage(DnsMessage message)
	{
		using var stream = BufferPool.RentMemoryStream();
		message.WriteToStream(stream);
		var output = new byte[stream.Position];
		Buffer.BlockCopy(stream.GetBuffer(), 0, output, 0, output.Length);
		return output;
	}

	private void SendUdpResponse(DnsMessage message, EndPoint remoteEndPoint)
	{
		var payload = SerializeMessage(message);
		Interlocked.Increment(ref _responses);
		SendUdp(payload, 0, payload.Length, remoteEndPoint);
	}

	private bool TryResolveZone(string hostName, out Zone zone)
	{
		zone = null;
		if (_resolvers == null) return false;

		foreach (var resolver in _resolvers)
			if (resolver.TryGetZone(hostName, out zone) && zone != null)
				return true;

		return false;
	}

	private static DnsMessage BuildBasicResponse(
		DnsMessage request,
		byte rCode,
		bool authoritative,
		bool recursionAvailable
	)
	{
		var response = new DnsMessage
		{
			QueryIdentifier = request.QueryIdentifier,
			Opcode          = request.Opcode,
			RD              = request.RD,
			RCode           = rCode,
			QR              = true,
			AA              = authoritative,
			RA              = recursionAvailable,
		};

		foreach (var question in request.Questions)
			response.Questions.Add(new(question.Name, question.Type, question.Class));
		response.QuestionCount = (ushort)response.Questions.Count;

		return response;
	}

	private ResourceRecord CreateSoaRecord(string name, Zone zone, ZoneRecord zoneRecord = null)
	{
		zoneRecord ??= zone.Records.FirstOrDefault(record => record.Type == ResourceType.SOA);
		var soa = ParseSoaFields(name, zone, zoneRecord);
		return new()
		{
			Name  = name,
			Class = ResourceClass.IN,
			Type  = ResourceType.SOA,
			TTL   = soa.MinimumTtl,
			RData = new SOARData
			{
				PrimaryNameServer               = soa.PrimaryNameServer,
				ResponsibleAuthoritativeMailbox = soa.ResponsibleMailbox,
				Serial                          = zone.Serial,
				ExpirationLimit                 = soa.Expire,
				RetryInterval                   = soa.Retry,
				RefreshInterval                 = soa.Refresh,
				MinimumTTL                      = soa.MinimumTtl,
			},
		};
	}

	private static SoaFields ParseSoaFields(string zoneName, Zone zone, ZoneRecord zoneRecord)
	{
		const uint defaultRefresh = 3600;
		const uint defaultRetry   = 600;
		const uint defaultExpire  = 1209600;
		const uint defaultMinimum = 300;

		var primaryNameServer  = zoneRecord?.Addresses.ElementAtOrDefault(0);
		var responsibleMailbox = zoneRecord?.Addresses.ElementAtOrDefault(1);
		var refresh            = defaultRefresh;
		var retry              = defaultRetry;
		var expire             = defaultExpire;
		var minimum            = defaultMinimum;

		if (zoneRecord?.Addresses.Count == 1)
		{
			var tokens = zoneRecord.Addresses[0]
			                       .Replace("(", " ", StringComparison.Ordinal)
			                       .Replace(")", " ", StringComparison.Ordinal)
			                       .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
			if (tokens.Length >= 7)
			{
				primaryNameServer  = tokens[0];
				responsibleMailbox = tokens[1];
				refresh            = ParseSoaInterval(tokens[3], defaultRefresh);
				retry              = ParseSoaInterval(tokens[4], defaultRetry);
				expire             = ParseSoaInterval(tokens[5], defaultExpire);
				minimum            = ParseSoaInterval(tokens[6], defaultMinimum);
			}
		}
		else if (zoneRecord?.Addresses.Count >= 6)
		{
			refresh = ParseSoaInterval(zoneRecord.Addresses[2], defaultRefresh);
			retry   = ParseSoaInterval(zoneRecord.Addresses[3], defaultRetry);
			expire  = ParseSoaInterval(zoneRecord.Addresses[4], defaultExpire);
			minimum = ParseSoaInterval(zoneRecord.Addresses[5], defaultMinimum);
		}

		primaryNameServer = primaryNameServer?.Trim().TrimEnd('.');
		if (string.IsNullOrWhiteSpace(primaryNameServer))
			primaryNameServer = zone.Records.Where(record => record.Type == ResourceType.NS)
			                        .SelectMany(record => record.Addresses)
			                        .FirstOrDefault()
			                        ?.Trim()
			                        .TrimEnd('.') ??
			                    $"ns1.{zoneName}";
		if (!primaryNameServer.Contains('.'))
			primaryNameServer = $"{primaryNameServer}.{zoneName}";

		responsibleMailbox = responsibleMailbox?.Trim().TrimEnd('.');
		if (string.IsNullOrWhiteSpace(responsibleMailbox)) responsibleMailbox = $"hostmaster.{zoneName}";
		else if (!responsibleMailbox.Contains('.')) responsibleMailbox        = $"{responsibleMailbox}.{zoneName}";

		return new(
			primaryNameServer,
			responsibleMailbox,
			refresh,
			retry,
			expire,
			minimum
		);
	}

	private static uint ParseSoaInterval(string value, uint fallback)
	{
		if (string.IsNullOrWhiteSpace(value)) return fallback;
		var normalized = value.Trim().ToUpperInvariant();
		if (uint.TryParse(normalized, out var seconds)) return seconds;
		if (normalized.Length < 2 || !uint.TryParse(normalized[..^1], out var quantity)) return fallback;

		var multiplier = normalized[^1] switch
		{
			'S' => 1UL,
			'M' => 60UL,
			'H' => 3600UL,
			'D' => 86400UL,
			'W' => 604800UL,
			_   => 0UL,
		};
		var total = quantity * multiplier;
		return multiplier > 0 && total <= uint.MaxValue ? (uint)total : fallback;
	}

	private ResourceRecord CreateNsRecord(string zoneName, Zone zone, ZoneRecord zoneRecord = null)
	{
		var primaryNameServer = zoneRecord?.Addresses.Count > 0
			? zoneRecord.Addresses[0]
			: $"{Environment.MachineName}.{zoneName}";

		var normalizedPrimaryNameServer = primaryNameServer?.Trim().TrimEnd('.');
		if (string.IsNullOrWhiteSpace(normalizedPrimaryNameServer))
			normalizedPrimaryNameServer = $"ns1.{zoneName}";

		if (!normalizedPrimaryNameServer.Contains('.'))
			normalizedPrimaryNameServer = $"{normalizedPrimaryNameServer}.{zoneName}";

		return new()
		{
			Name  = zoneName,
			Class = ResourceClass.IN,
			Type  = ResourceType.NS,
			TTL   = 300,
			RData = new NSRData { Name = normalizedPrimaryNameServer },
		};
	}

	private ResourceRecord CreateInjectedNsAddressRecord(ResourceRecord nsRecord, string zoneName)
	{
		if (nsRecord?.RData is not NSRData nsRData) return null;

		var configuredAddress = serverOptions.Value.ZoneTransfer.InjectedNsAddress?.Trim();
		if (string.IsNullOrWhiteSpace(configuredAddress)) return null;

		var nsOwnerName = nsRData.Name?.Trim().TrimEnd('.');
		if (string.IsNullOrWhiteSpace(nsOwnerName)) return null;

		if (IPAddress.TryParse(configuredAddress, out var ipAddress))
		{
			var recordType = ipAddress.AddressFamily == AddressFamily.InterNetworkV6
				? ResourceType.AAAA
				: ResourceType.A;

			return new()
			{
				Name  = nsOwnerName,
				Class = ResourceClass.IN,
				Type  = recordType,
				TTL   = 300,
				RData = new ANameRData { Address = ipAddress },
			};
		}

		var cnameTarget                             = configuredAddress.TrimEnd('.');
		if (!cnameTarget.Contains('.')) cnameTarget = $"{cnameTarget}.{zoneName}";

		return new()
		{
			Name  = nsOwnerName,
			Class = ResourceClass.IN,
			Type  = ResourceType.CNAME,
			TTL   = 300,
			RData = new CNameRData { Name = cnameTarget },
		};
	}

	private bool IsTransferAllowed(EndPoint remoteEndPoint)
	{
		if (remoteEndPoint is not IPEndPoint ipEndpoint) return false;

		var allowList = serverOptions.Value.ZoneTransfer.AllowTransfersFrom;
		if (allowList == null || allowList.Count == 0) return false;

		return allowList.Any(entry => IsAllowedByEntry(ipEndpoint.Address, entry));
	}

	private bool IsRecursionAllowed(EndPoint remoteEndPoint)
	{
		if (serverOptions.Value.DnsListener.RecursionEnabled) return true;
		if (remoteEndPoint is not IPEndPoint ipEndpoint) return false;

		var allowList = serverOptions.Value.DnsListener.AllowRecursionFrom;
		return allowList != null && allowList.Any(entry => IsAllowedByEntry(ipEndpoint.Address, entry));
	}

	private bool IsAllowedByEntry(IPAddress remoteAddress, string allowEntry)
	{
		if (string.IsNullOrWhiteSpace(allowEntry)) return false;

		var normalizedEntry = allowEntry.Trim();
		if (normalizedEntry == "*") return true;

		if (normalizedEntry.Contains('/'))
		{
			var split = normalizedEntry.Split('/');
			if (split.Length != 2) return false;
			if (!IPAddress.TryParse(split[0], out var networkAddress)) return false;
			if (!int.TryParse(split[1], out var prefixLength)) return false;
			return IsAddressInCidr(remoteAddress, networkAddress, prefixLength);
		}

		if (IPAddress.TryParse(normalizedEntry, out var exactAddress))
			return AreEquivalentAddresses(remoteAddress, exactAddress);

		var hostName = normalizedEntry.TrimEnd('.');
		if (Uri.CheckHostName(hostName) != UriHostNameType.Dns) return false;

		return ResolveHostAddresses(hostName).Any(address => AreEquivalentAddresses(remoteAddress, address));
	}

	private IReadOnlyList<IPAddress> ResolveHostAddresses(string hostName)
	{
		var now = DateTimeOffset.UtcNow;
		if (_hostAddressCache.TryGetValue(hostName, out var cached) && cached.ExpiresAt > now)
			return cached.Addresses;

		IPAddress[] addresses;
		try
		{
			addresses = _hostAddressResolver(hostName)
			            .Where(address => address.AddressFamily is AddressFamily.InterNetwork
				                   or AddressFamily.InterNetworkV6
			            )
			            .Distinct()
			            .ToArray();
		}
		catch (Exception ex) when (ex is SocketException or ArgumentException)
		{
			logger.LogWarning(ex, "Unable to resolve configured DNS hostname {HostName}", hostName);
			addresses = [];
		}

		_hostAddressCache[hostName] = new(now.Add(TransferHostAddressCacheDuration), addresses);
		return addresses;
	}

	private static bool AreEquivalentAddresses(IPAddress left, IPAddress right)
	{
		if (left.IsIPv4MappedToIPv6) left   = left.MapToIPv4();
		if (right.IsIPv4MappedToIPv6) right = right.MapToIPv4();

		return left.Equals(right);
	}

	private static bool IsAddressInCidr(IPAddress remoteAddress, IPAddress networkAddress, int prefixLength)
	{
		if (remoteAddress.IsIPv4MappedToIPv6 && networkAddress.AddressFamily == AddressFamily.InterNetwork)
			remoteAddress = remoteAddress.MapToIPv4();
		else if (networkAddress.IsIPv4MappedToIPv6 && remoteAddress.AddressFamily == AddressFamily.InterNetwork)
			remoteAddress = remoteAddress.MapToIPv6();

		var remoteBytes  = remoteAddress.GetAddressBytes();
		var networkBytes = networkAddress.GetAddressBytes();
		if (remoteBytes.Length != networkBytes.Length) return false;
		if (prefixLength < 0 || prefixLength > remoteBytes.Length * 8) return false;

		var fullBytes = prefixLength / 8;
		var extraBits = prefixLength % 8;

		for (var i = 0; i < fullBytes; i++)
			if (remoteBytes[i] != networkBytes[i])
				return false;

		if (extraBits == 0) return true;

		var mask = (byte)~(0xFF >> extraBits);
		return (remoteBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
	}

	private sealed record HostAddressCacheEntry(DateTimeOffset ExpiresAt, IPAddress[] Addresses);

	private List<IPEndPoint> ParseNotifyTargets(IEnumerable<string> entries)
	{
		var endpoints = new List<IPEndPoint>();
		if (entries == null) return endpoints;

		foreach (var entry in entries)
		{
			if (string.IsNullOrWhiteSpace(entry)) continue;

			if (!TryParseNotifyTarget(entry, out var hostName, out var port)) continue;

			if (IPAddress.TryParse(hostName, out var ipAddress))
			{
				endpoints.Add(new(ipAddress, port));
				continue;
			}

			endpoints.AddRange(ResolveHostAddresses(hostName).Select(address => new IPEndPoint(address, port)));
		}

		return endpoints.Distinct().ToList();
	}

	private static bool TryParseNotifyTarget(string value, out string hostName, out ushort port)
	{
		hostName = null;
		port     = 53;
		var trimmed = value.Trim();
		if (trimmed.StartsWith("[", StringComparison.Ordinal))
		{
			var closingBracket = trimmed.IndexOf(']');
			if (closingBracket <= 1) return false;

			hostName = trimmed[1..closingBracket];
			if (closingBracket < trimmed.Length - 1 &&
			    (trimmed[closingBracket + 1] != ':' ||
			     !ushort.TryParse(trimmed[(closingBracket + 2)..], out port) ||
			     port == 0))
				return false;

			return IPAddress.TryParse(hostName, out _);
		}

		if (IPAddress.TryParse(trimmed, out _))
		{
			hostName = trimmed;
			return true;
		}

		var separator = trimmed.LastIndexOf(':');
		if (separator > 0 && trimmed.IndexOf(':') == separator)
		{
			if (!ushort.TryParse(trimmed[(separator + 1)..], out port) || port == 0) return false;
			trimmed = trimmed[..separator];
		}

		hostName = trimmed.TrimEnd('.');
		return IPAddress.TryParse(hostName, out _) || Uri.CheckHostName(hostName) == UriHostNameType.Dns;
	}

	private async Task RunNotifyLoop()
	{
		var pollInterval = Math.Max(1, serverOptions.Value.ZoneTransfer.NotifyPollIntervalSeconds);

		while (!_notifyLoopCancellationToken.IsCancellationRequested)
		{
			try
			{
				_notifyTargets = ParseNotifyTargets(_notifyTargetEntries);
				var zones = _resolvers.SelectMany(resolver => resolver.GetZones()).ToList();
				foreach (var zone in zones.Where(zone => zone != null))
				{
					var zoneKey = CanonicalZoneName(zone.Suffix);
					var existed = _zoneSerials.TryGetValue(zoneKey, out var previousSerial);
					_zoneSerials[zoneKey] = zone.Serial;

					if (!existed || previousSerial == zone.Serial) continue;

					foreach (var notifyTarget in _notifyTargets)
						SendNotify(zone, zoneKey, notifyTarget);
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "notify loop error");
			}

			try
			{
				await Task.Delay(TimeSpan.FromSeconds(pollInterval), _notifyLoopCancellationToken)
				          .ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private void SendNotify(Zone zone, string zoneName, IPEndPoint notifyTarget)
	{
		var notifyMessage = new DnsMessage
		{
			QueryIdentifier = (ushort)Random.Shared.Next(ushort.MinValue, ushort.MaxValue + 1),
			Opcode          = (byte)OpCode.NOTIFY,
			AA              = true,
			QuestionCount   = 1,
		};
		notifyMessage.Questions.Add(new(zoneName, ResourceType.SOA, ResourceClass.IN));
		notifyMessage.Answers.Add(CreateSoaRecord(zoneName, zone));
		notifyMessage.AnswerCount = 1;

		var payload = SerializeMessage(notifyMessage);
		SendUdp(payload, 0, payload.Length, notifyTarget);
	}

	private void HandleRecords(List<ZoneRecord> zoneRecords, Question question, DnsMessage message, Zone zone)
	{
		foreach (var zoneRecord in zoneRecords)
		{
			switch (zoneRecord.Type)
			{
				case ResourceType.NS:
					foreach (var answer in zoneRecord.Addresses.Select(address => new ResourceRecord
						         {
							         Name  = question.Name,
							         Class = zoneRecord.Class,
							         Type  = zoneRecord.Type,
							         TTL   = 10,
							         RData = new NSRData { Name = address },
						         }
					         ))
					{
						message.AnswerCount++;
						message.Answers.Add(answer);
						AddNsGlueRecords(message, zone, answer);
					}

					break;
				case ResourceType.MX:
					foreach (var answer in zoneRecord.Addresses.Select(address =>
						         {
							         var addressSplit = address.Split(' ');
							         var tmpRecord = new ResourceRecord
							         {
								         Name  = question.Name,
								         Class = zoneRecord.Class,
								         Type  = zoneRecord.Type,
								         TTL   = 10,
								         RData = new MXRData
								         {
									         Name = addressSplit[1], Preference = Convert.ToUInt16(addressSplit[0])
								         },
							         };

							         return tmpRecord;
						         }
					         ))
					{
						message.AnswerCount++;
						message.Answers.Add(answer);
					}

					break;
				case ResourceType.A:
					foreach (var answer in zoneRecord.Addresses.Select(address => new ResourceRecord
						         {
							         Name  = question.Name,
							         Class = zoneRecord.Class,
							         Type  = zoneRecord.Type,
							         TTL   = 10,
							         RData = new ANameRData
							         {
								         Address = IPAddress.Parse(
									         address
								         )
							         },
						         }
					         ))
					{
						message.AnswerCount++;
						message.Answers.Add(answer);
					}

					break;
				case ResourceType.CNAME:
					var zoneName = CanonicalZoneName(zone.Suffix);
					foreach (var answer in zoneRecord.Addresses.Select(address => new ResourceRecord
						         {
							         Name  = question.Name,
							         Class = zoneRecord.Class,
							         Type  = zoneRecord.Type,
							         TTL   = 10,
							         RData = new CNameRData
							         {
								         Name = NormalizeAliasTarget(
									         address,
									         zoneName
								         )
							         },
						         }
					         ))
					{
						message.AnswerCount++;
						message.Answers.Add(answer);
						if (answer.RData is CNameRData cnameRData && cnameRData.Name.Contains(zone.Suffix))
						{
							var address = cnameRData.Name.Replace($".{zone.Suffix}.", "")
							                        .Replace($".{zone.Suffix}", "")
							                        .Replace($"{zone.Suffix}", "");

							var addressType = question.Type == ResourceType.AAAA ? ResourceType.AAAA : ResourceType.A;
							var cnameRecords = zone.Records
							                       .Where(record => record.Type == addressType &&
							                                        string.Equals(
								                                        record.Host,
								                                        address,
								                                        StringComparison.OrdinalIgnoreCase
							                                        )
							                       )
							                       .ToList();
							HandleRecords(
								cnameRecords,
								new(cnameRData.Name, addressType, ResourceClass.IN),
								message,
								zone
							);
						}
					}

					break;
				case ResourceType.SOA:
					var soaAnswer = CreateSoaRecord(question.Name, zone, zoneRecord);

					message.AnswerCount++;
					message.Answers.Add(soaAnswer);
					break;
				case ResourceType.TXT:
					foreach (var answer in zoneRecord.Addresses.Select(address => new ResourceRecord
						         {
							         Name  = question.Name,
							         Class = zoneRecord.Class,
							         Type  = zoneRecord.Type,
							         TTL   = 10,
							         RData = new TXTRData { Name = address },
						         }
					         ))
					{
						message.AnswerCount++;
						message.Answers.Add(answer);
					}

					break;
				case ResourceType.PTR:
					foreach (var answer in zoneRecord.Addresses.Select(address => new ResourceRecord
						         {
							         Name  = question.Name,
							         Class = zoneRecord.Class,
							         Type  = zoneRecord.Type,
							         TTL   = 10,
							         RData = new DomainNamePointRData
							         {
								         Name = address
							         },
						         }
					         ))
					{
						message.AnswerCount++;
						message.Answers.Add(answer);
					}

					break;
			}
		}
	}

	private static void AddNsGlueRecords(DnsMessage message, Zone zone, ResourceRecord nsRecord)
	{
		if (nsRecord.RData is not NSRData nsData) return;

		var zoneName = CanonicalZoneName(zone.Suffix);
		var target   = CanonicalZoneName(nsData.Name);
		if (!target.EndsWith($".{zoneName}", StringComparison.OrdinalIgnoreCase) &&
		    !string.Equals(target, zoneName, StringComparison.OrdinalIgnoreCase))
			return;

		foreach (var glueSource in zone.Records.Where(record => record.Type is ResourceType.A or ResourceType.AAAA &&
		                                                        string.Equals(
			                                                        CanonicalZoneName(
				                                                        BuildRecordOwnerName(zoneName, record.Host)
			                                                        ),
			                                                        target,
			                                                        StringComparison.OrdinalIgnoreCase
		                                                        )
		         ))
		foreach (var address in glueSource.Addresses)
		{
			message.Additionals.Add(
				new()
				{
					Name  = target,
					Class = glueSource.Class,
					Type  = glueSource.Type,
					TTL   = 10,
					RData = new ANameRData { Address = IPAddress.Parse(address) },
				}
			);
			message.AdditionalCount++;
		}
	}

	/// <summary>Send UDP response via UDP listener socket</summary>
	/// <param name="bytes">The buffer containing the data to send.</param>
	/// <param name="offset">The offset in the buffer where data starts.</param>
	/// <param name="count">The number of bytes to send.</param>
	/// <param name="remoteEndpoint">The destination endpoint.</param>
	private void SendUdp(byte[] bytes, int offset, int count, EndPoint remoteEndpoint)
	{
		_ = SendUdpAsync(bytes, offset, count, remoteEndpoint);
	}

	private async Task SendUdpAsync(byte[] bytes, int offset, int count, EndPoint remoteEndpoint)
	{
		var args = BufferPool.RentSocketAsyncEventArgs();
		args.RemoteEndPoint = remoteEndpoint;

		var sendBuffer = new byte[count];
		Buffer.BlockCopy(bytes, offset, sendBuffer, 0, count);
		args.SetBuffer(sendBuffer, 0, count);

		try
		{
			await _udpListener.SendToAsync(args).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
		{
			logger.LogWarning(ex, "Unable to send UDP packet to {@RemoteEndPoint}", remoteEndpoint);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Unexpected error while sending UDP packet to {@RemoteEndPoint}", remoteEndpoint);
		}
		finally
		{
			BufferPool.ReturnSocketAsyncEventArgs(args);
		}
	}

	/// <summary>Returns list of manual or DHCP specified DNS addresses</summary>
	/// <returns>List of configured DNS names</returns>
	private IEnumerable<IPAddress> GetDefaultDNS()
	{
		var adapters = NetworkInterface.GetAllNetworkInterfaces();
		foreach (var adapter in adapters)
		{
			var adapterProperties = adapter.GetIPProperties();
			var dnsServers        = adapterProperties.DnsAddresses;

			foreach (var dns in dnsServers)
			{
				logger.LogInformation("Discovered DNS: {Dns}", dns);

				yield return dns;
			}
		}
	}
}