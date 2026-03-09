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
using System.Threading;
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
	/// <summary>
	///     Maps forwarded DNS requests to their originating endpoints.
	/// </summary>
	private readonly ConcurrentDictionary<DnsRequestKey, EndPoint> _requestResponseMap = new();
	private readonly Dictionary<string, uint> _zoneSerials = new(StringComparer.OrdinalIgnoreCase);

	private IPAddress[] _defaultDns;
	private long _nacks;
	private CancellationToken _notifyLoopCancellationToken;
	private List<IPEndPoint> _notifyTargets = [];
	private long _requests;
	private List<IDnsResolver> _resolvers; // resolver for name entries
	private long _responses;
	private TcpDnsListener _tcpListener;
	private UdpListener _udpListener; // listener for UDP53 traffic

	/// <summary>Initialize server with specified domain name resolver</summary>
	/// <param name="resolvers"></param>
	public void Initialize(List<IDnsResolver> resolvers)
	{
		_resolvers = resolvers;

		_udpListener = new();
		_tcpListener = new();

		_udpListener.Initialize(serverOptions.Value.DnsListener.Port);
		_udpListener.OnRequest += ProcessUdpRequest;
		var tcpPort = serverOptions.Value.DnsListener.TcpPort ?? serverOptions.Value.DnsListener.Port;
		_tcpListener.Initialize(tcpPort);
		_tcpListener.OnRequest += ProcessTcpRequest;

		_defaultDns = GetDefaultDNS().ToArray();
		_notifyTargets = ParseNotifyTargets(serverOptions.Value.ZoneTransfer.NotifySecondaries);
	}

	public Task Start(CancellationToken ct)
	{
		_udpListener.Start();
		_tcpListener.Start();
		ct.Register(_udpListener.Stop);
		ct.Register(_tcpListener.Stop);

		if (serverOptions.Value.ZoneTransfer.Enabled && _notifyTargets.Count > 0)
		{
			_notifyLoopCancellationToken = ct;
			_ = Task.Run(RunNotifyLoop, ct);
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
			if (message.Questions.Count <= 0) return;
			Zone zone = null;

			foreach (var question in message.Questions)
			{
				logger.LogInformation(
					"{@RemoteEndPoint} asked for {Name} {Class} {Type}",
					remoteEndPoint,
					question.Name,
					question.Class,
					question.Type
				);
				if (question.Type == ResourceType.PTR && question.Name == "1.0.0.127.in-addr.arpa")
				{
					message.QR = true;
					message.AA = true;
					message.RA = false;
					message.AnswerCount++;
					message.Answers.Add(
						new()
						{
							Name = question.Name,
							Class = ResourceClass.IN,
							Type = ResourceType.PTR,
							TTL = 3600,
							DataLength = 0xB,
							RData = new DomainNamePointRData { Name = "localhost" },
						}
					);
				}
				else if (_resolvers.FirstOrDefault(x => x.TryGetZone(
													   question.Name,
													   out zone
												   )
						 ) !=
						 null)
				{
					var qName = question.Name.Replace($".{zone.Suffix}", "").Replace($"{zone.Suffix}", "");
					message.QR = true;
					message.AA = true;
					message.RA = false;
					message.RCode = (byte)RCode.NOERROR;
					var zoneRecords = question.Type switch
					{
						ResourceType.ANY => zone.Records.Where(zr => zr.Host.Equals(qName)).ToList(),
						ResourceType.A => zone.Records.Where(zr => zr.Type is ResourceType.A or ResourceType.CNAME && zr.Host.Equals(qName)).ToList(),
						ResourceType.AAAA => zone.Records.Where(zr => zr.Type is ResourceType.AAAA or ResourceType.CNAME && zr.Host.Equals(qName)).ToList(),
						_ => zone.Records.Where(zr => zr.Type == question.Type && zr.Host.Equals(qName)).ToList(),
					};

					if (zoneRecords.Count == 0)
					{
						var zoneName = CanonicalZoneName(zone.Suffix);
						var zoneSoa = zone.Records.FirstOrDefault(record => record.Type == ResourceType.SOA);
						var isZoneApexQuery = string.Equals(
							question.Name.Trim().TrimEnd('.'),
							zoneName,
							StringComparison.OrdinalIgnoreCase
						);

						if (question.Type == ResourceType.SOA && isZoneApexQuery)
						{
							message.RCode = (byte)RCode.NOERROR;
							message.AnswerCount++;
							message.Answers.Add(CreateSoaRecord(zoneName, zone, zoneSoa));
						}
						else
						{
							var nameExists = isZoneApexQuery ||
											 zone.Records.Any(record =>
												 string.Equals(record.Host, qName, StringComparison.OrdinalIgnoreCase)
											 );

							message.RCode = (byte)(nameExists ? RCode.NOERROR : RCode.NXDOMAIN);
							message.NameServerCount++;
							message.Authorities.Add(CreateSoaRecord(zoneName, zone, zoneSoa));
						}
					}
					else
					{
						HandleRecords(zoneRecords, question, message, zone, remoteEndPoint);
					}
				}
				else // Referral to regular DC DNS servers
				{
					// store current IP address and Query ID.
					var key = new DnsRequestKey(message);
					_requestResponseMap.TryAdd(key, remoteEndPoint);
				}

				using var responseStream = BufferPool.RentMemoryStream();

				message.WriteToStream(responseStream);
				if (message.IsQuery())
				{
					// send to upstream DNS servers
					foreach (var dnsServer in _defaultDns)
						SendUdp(
							responseStream.GetBuffer(),
							0,
							(int)responseStream.Position,
							new IPEndPoint(dnsServer, 53)
						);
				}
				else
				{
					Interlocked.Increment(ref _responses);
					SendUdp(responseStream.GetBuffer(), 0, (int)responseStream.Position, remoteEndPoint);
				}
			}
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

		return BuildBasicResponse(message, (byte)RCode.REFUSED, authoritative: false, recursionAvailable: false);
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
		var clientSerial = message.Authorities
								  .Where(authority => authority.Type == ResourceType.SOA)
								  .Select(authority => authority.RData)
								  .OfType<SOARData>()
								  .Select(soa => (uint?)soa.Serial)
								  .FirstOrDefault();

		if (clientSerial.HasValue && clientSerial.Value >= zone.Serial) return [CreateSoaRecord(zoneName, zone)];

		return BuildAxfrRecords(zone, zoneName);
	}

	private List<ResourceRecord> BuildAxfrRecords(Zone zone, string zoneName)
	{
		var answers = new List<ResourceRecord> { CreateSoaRecord(zoneName, zone) };

		foreach (var zoneRecord in zone.Records)
		{
			foreach (var rr in BuildResourceRecords(zoneRecord, zone, zoneName))
			{
				if (rr.Type == ResourceType.SOA) continue;
				answers.Add(rr);
			}
		}

		if (!answers.Any(record =>
				record.Type == ResourceType.NS &&
				string.Equals(record.Name, zoneName, StringComparison.OrdinalIgnoreCase)
			))
		{
			var nsRecord = CreateNsRecord(zoneName, zone);
			answers.Add(nsRecord);
			var nsAddressRecord = CreateInjectedNsAddressRecord(nsRecord, zoneName);
			if (nsAddressRecord != null) answers.Add(nsAddressRecord);
		}

		answers.Add(CreateSoaRecord(zoneName, zone));
		return answers;
	}

	private List<ResourceRecord> BuildResourceRecords(ZoneRecord zoneRecord, Zone zone, string zoneName)
	{
		var name = BuildRecordOwnerName(zoneName, zoneRecord.Host);
		var records = new List<ResourceRecord>();

		switch (zoneRecord.Type)
		{
			case ResourceType.NS:
				records.AddRange(zoneRecord.Addresses.Select(address => new ResourceRecord
				{
					Name = name,
					Class = zoneRecord.Class,
					Type = zoneRecord.Type,
					TTL = 10,
					RData = new NSRData { Name = address },
				}
				));
				break;
			case ResourceType.MX:
				records.AddRange(zoneRecord.Addresses.Select(address =>
					{
						var addressSplit = address.Split(' ');
						return new ResourceRecord
						{
							Name = name,
							Class = zoneRecord.Class,
							Type = zoneRecord.Type,
							TTL = 10,
							RData = new MXRData { Name = addressSplit[1], Preference = Convert.ToUInt16(addressSplit[0]) },
						};
					}
				));
				break;
			case ResourceType.A:
				records.AddRange(zoneRecord.Addresses.Select(address => new ResourceRecord
				{
					Name = name,
					Class = zoneRecord.Class,
					Type = zoneRecord.Type,
					TTL = 10,
					RData = new ANameRData { Address = IPAddress.Parse(address) },
				}
				));
				break;
			case ResourceType.CNAME:
				records.AddRange(zoneRecord.Addresses.Select(address => new ResourceRecord
				{
					Name = name,
					Class = zoneRecord.Class,
					Type = zoneRecord.Type,
					TTL = 10,
					RData = new CNameRData { Name = NormalizeAliasTarget(address, zoneName) },
				}
				));
				break;
			case ResourceType.TXT:
				records.AddRange(zoneRecord.Addresses.Select(address => new ResourceRecord
				{
					Name = name,
					Class = zoneRecord.Class,
					Type = zoneRecord.Type,
					TTL = 10,
					RData = new TXTRData { Name = address },
				}
				));
				break;
			case ResourceType.PTR:
				records.AddRange(zoneRecord.Addresses.Select(address => new ResourceRecord
				{
					Name = name,
					Class = zoneRecord.Class,
					Type = zoneRecord.Type,
					TTL = 10,
					RData = new DomainNamePointRData { Name = address },
				}
				));
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
			Opcode = request.Opcode,
			RD = request.RD,
			RCode = rCode,
			QR = true,
			AA = authoritative,
			RA = recursionAvailable,
		};

		foreach (var question in request.Questions)
			response.Questions.Add(new(question.Name, question.Type, question.Class));
		response.QuestionCount = (ushort)response.Questions.Count;

		return response;
	}

	private ResourceRecord CreateSoaRecord(string name, Zone zone, ZoneRecord zoneRecord = null)
	{
		return new()
		{
			Name = name,
			Class = ResourceClass.IN,
			Type = ResourceType.SOA,
			TTL = 300,
			RData = new SOARData
			{
				PrimaryNameServer = zoneRecord?.Addresses.Count > 1
					? zoneRecord.Addresses[0]
					: Environment.MachineName,
				ResponsibleAuthoritativeMailbox = zoneRecord?.Addresses.Count > 1
					? zoneRecord.Addresses[1]
					: zoneRecord?.Addresses.FirstOrDefault() ?? $"hostmaster.{name}",
				Serial = zone.Serial,
				ExpirationLimit = 86400,
				RetryInterval = 300,
				RefreshInterval = 300,
				MinimumTTL = 300,
			},
		};
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
			Name = zoneName,
			Class = ResourceClass.IN,
			Type = ResourceType.NS,
			TTL = 300,
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
				Name = nsOwnerName,
				Class = ResourceClass.IN,
				Type = recordType,
				TTL = 300,
				RData = new ANameRData { Address = ipAddress },
			};
		}

		var cnameTarget = configuredAddress.TrimEnd('.');
		if (!cnameTarget.Contains('.')) cnameTarget = $"{cnameTarget}.{zoneName}";

		return new()
		{
			Name = nsOwnerName,
			Class = ResourceClass.IN,
			Type = ResourceType.CNAME,
			TTL = 300,
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

	private static bool IsAllowedByEntry(IPAddress remoteAddress, string allowEntry)
	{
		if (string.IsNullOrWhiteSpace(allowEntry)) return false;
		if (allowEntry == "*") return true;

		if (allowEntry.Contains('/'))
		{
			var split = allowEntry.Split('/');
			if (split.Length != 2) return false;
			if (!IPAddress.TryParse(split[0], out var networkAddress)) return false;
			if (!int.TryParse(split[1], out var prefixLength)) return false;
			return IsAddressInCidr(remoteAddress, networkAddress, prefixLength);
		}

		return IPAddress.TryParse(allowEntry, out var exactAddress) && exactAddress.Equals(remoteAddress);
	}

	private static bool IsAddressInCidr(IPAddress remoteAddress, IPAddress networkAddress, int prefixLength)
	{
		var remoteBytes = remoteAddress.GetAddressBytes();
		var networkBytes = networkAddress.GetAddressBytes();
		if (remoteBytes.Length != networkBytes.Length) return false;

		var fullBytes = prefixLength / 8;
		var extraBits = prefixLength % 8;

		for (var i = 0; i < fullBytes; i++)
			if (remoteBytes[i] != networkBytes[i])
				return false;

		if (extraBits == 0) return true;

		var mask = (byte)~(0xFF >> extraBits);
		return (remoteBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
	}

	private static List<IPEndPoint> ParseNotifyTargets(IEnumerable<string> entries)
	{
		var endpoints = new List<IPEndPoint>();
		if (entries == null) return endpoints;

		foreach (var entry in entries)
		{
			if (string.IsNullOrWhiteSpace(entry)) continue;

			if (TryParseIpEndpoint(entry, out var endpoint))
				endpoints.Add(endpoint);
		}

		return endpoints;
	}

	private static bool TryParseIpEndpoint(string value, out IPEndPoint endpoint)
	{
		endpoint = null;
		var trimmed = value.Trim();
		if (IPAddress.TryParse(trimmed, out var ipAddress))
		{
			endpoint = new(ipAddress, 53);
			return true;
		}

		var separator = trimmed.LastIndexOf(':');
		if (separator <= 0 || separator == trimmed.Length - 1) return false;

		var hostPart = trimmed[..separator];
		var portPart = trimmed[(separator + 1)..];
		if (!IPAddress.TryParse(hostPart, out ipAddress)) return false;
		if (!ushort.TryParse(portPart, out var port)) return false;

		endpoint = new(ipAddress, port);
		return true;
	}

	private async Task RunNotifyLoop()
	{
		var pollInterval = Math.Max(1, serverOptions.Value.ZoneTransfer.NotifyPollIntervalSeconds);

		while (!_notifyLoopCancellationToken.IsCancellationRequested)
		{
			try
			{
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
				await Task.Delay(TimeSpan.FromSeconds(pollInterval), _notifyLoopCancellationToken).ConfigureAwait(false);
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
			Opcode = (byte)OpCode.NOTIFY,
			AA = true,
			QuestionCount = 1,
		};
		notifyMessage.Questions.Add(new(zoneName, ResourceType.SOA, ResourceClass.IN));
		notifyMessage.Answers.Add(CreateSoaRecord(zoneName, zone));
		notifyMessage.AnswerCount = 1;

		var payload = SerializeMessage(notifyMessage);
		SendUdp(payload, 0, payload.Length, notifyTarget);
	}

	private void HandleRecords(
		List<ZoneRecord> zoneRecords,
		Question question,
		DnsMessage message,
		Zone zone,
		EndPoint remoteEndPoint
	)
	{
		foreach (var zoneRecord in zoneRecords)
		{
			switch (zoneRecord.Type)
			{
				case ResourceType.NS:
					foreach (var answer in zoneRecord.Addresses.Select(address => new ResourceRecord
					{
						Name = question.Name,
						Class = zoneRecord.Class,
						Type = zoneRecord.Type,
						TTL = 10,
						RData = new NSRData { Name = address },
					}
							 ))
					{
						message.AnswerCount++;
						message.Answers.Add(answer);
					}

					break;
				case ResourceType.MX:
					foreach (var answer in zoneRecord.Addresses.Select(address =>
								 {
									 var addressSplit = address.Split(' ');
									 var tmpRecord = new ResourceRecord
									 {
										 Name = question.Name,
										 Class = zoneRecord.Class,
										 Type = zoneRecord.Type,
										 TTL = 10,
										 RData = new MXRData { Name = addressSplit[1], Preference = Convert.ToUInt16(addressSplit[0]) },
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
						Name = question.Name,
						Class = zoneRecord.Class,
						Type = zoneRecord.Type,
						TTL = 10,
						RData = new ANameRData { Address = IPAddress.Parse(address) },
					}
							 ))
					{
						message.AnswerCount++;
						message.Answers.Add(answer);
					}

					break;
				case ResourceType.CNAME:
					foreach (var answer in zoneRecord.Addresses.Select(address => new ResourceRecord
					{
						Name = question.Name,
						Class = zoneRecord.Class,
						Type = zoneRecord.Type,
						TTL = 10,
						RData = new CNameRData { Name = address },
					}
							 ))
					{
						message.AnswerCount++;
						message.Answers.Add(answer);
						if (answer.RData is CNameRData cnameRData && cnameRData.Name.Contains(zone.Suffix))
						{
							var address = cnameRData.Name
													.Replace($".{zone.Suffix}.", "")
													.Replace($".{zone.Suffix}", "")
													.Replace($"{zone.Suffix}", "");

							var cnameRecords = zone.Records
												   .Where(zr => zr.Type is ResourceType.A
																	or ResourceType.AAAA
																	or ResourceType.CNAME &&
																zr.Host.Equals(address)
												   )
												   .ToList();

							var dnsMessage = new DnsMessage
							{
								Opcode = (byte)OpCode.QUERY,
								Questions = [
									new(cnameRData.Name, ResourceType.A, ResourceClass.IN),
								],
							};
							using PooledMemoryStream pms = BufferPool.RentMemoryStream();
							dnsMessage.WriteToStream(pms);

							pms.Position = 0;

							ProcessUdpRequest(pms.ToArray(), (int)pms.Length, remoteEndPoint);

							//HandleRecords(cnameRecords, new Question(cnameRData.Name, ResourceType.A, ResourceClass.IN), message, zone);
						}
					}

					break;
				case ResourceType.SOA:
					var soaAnswer = new ResourceRecord
					{
						Name = question.Name,
						Class = zoneRecord.Class,
						Type = zoneRecord.Type,
						TTL = 10,
						RData = new SOARData
						{
							PrimaryNameServer = zoneRecord.Addresses.Count > 1 ? zoneRecord.Addresses[0] : Environment.MachineName,
							ResponsibleAuthoritativeMailbox = zoneRecord.Addresses.Count > 1 ? zoneRecord.Addresses[1] : zoneRecord.Addresses[0],
							Serial = zone.Serial,
							ExpirationLimit = 86400,
							RetryInterval = 300,
							RefreshInterval = 300,
							MinimumTTL = 300,
						},
					};
					soaAnswer.TTL = (soaAnswer.RData as SOARData).MinimumTTL;

					message.AnswerCount++;
					message.Answers.Add(soaAnswer);
					break;
				case ResourceType.TXT:
					foreach (var answer in zoneRecord.Addresses.Select(address => new ResourceRecord
					{
						Name = question.Name,
						Class = zoneRecord.Class,
						Type = zoneRecord.Type,
						TTL = 10,
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
						Name = question.Name,
						Class = zoneRecord.Class,
						Type = zoneRecord.Type,
						TTL = 10,
						RData = new DomainNamePointRData { Name = address },
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

	/// <summary>Send UDP response via UDP listener socket</summary>
	/// <param name="bytes">The buffer containing the data to send.</param>
	/// <param name="offset">The offset in the buffer where data starts.</param>
	/// <param name="count">The number of bytes to send.</param>
	/// <param name="remoteEndpoint">The destination endpoint.</param>
	private void SendUdp(byte[] bytes, int offset, int count, EndPoint remoteEndpoint)
	{
		// Get a pooled SocketAsyncEventArgs
		var args = BufferPool.RentSocketAsyncEventArgs();
		args.RemoteEndPoint = remoteEndpoint;

		// Copy data to a new buffer since the source may be reused
		// TODO: Future optimization - pool these send buffers too
		var sendBuffer = new byte[count];
		Buffer.BlockCopy(bytes, offset, sendBuffer, 0, count);
		args.SetBuffer(sendBuffer, 0, count);

		// Set up completion callback to return args to pool
		args.Completed += OnSendCompleted;

		_udpListener.SendToAsync(args);
	}

	/// <summary>Callback when send completes - returns SocketAsyncEventArgs to pool.</summary>
	private static void OnSendCompleted(object sender, SocketAsyncEventArgs args)
	{
		args.Completed -= OnSendCompleted;
		BufferPool.ReturnSocketAsyncEventArgs(args);
	}

	/// <summary>Returns list of manual or DHCP specified DNS addresses</summary>
	/// <returns>List of configured DNS names</returns>
	private IEnumerable<IPAddress> GetDefaultDNS()
	{
		var adapters = NetworkInterface.GetAllNetworkInterfaces();
		foreach (var adapter in adapters)
		{
			var adapterProperties = adapter.GetIPProperties();
			var dnsServers = adapterProperties.DnsAddresses;

			foreach (var dns in dnsServers)
			{
				logger.LogInformation("Discovered DNS: {Dns}", dns);

				yield return dns;
			}
		}
	}
}