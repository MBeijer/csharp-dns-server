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

	private IPAddress[]        _defaultDns;
	private long               _nacks;
	private long               _requests;
	private List<IDnsResolver> _resolvers; // resolver for name entries
	private long               _responses;
	private UdpListener        _udpListener; // listener for UDP53 traffic

	/// <summary>Initialize server with specified domain name resolver</summary>
	/// <param name="resolvers"></param>
	public void Initialize(List<IDnsResolver> resolvers)
	{
		_resolvers = resolvers;

		_udpListener = new();

		_udpListener.Initialize(serverOptions.Value.DnsListener.Port);
		_udpListener.OnRequest += ProcessUdpRequest;

		_defaultDns = GetDefaultDNS().ToArray();
	}

	public Task Start(CancellationToken ct)
	{
		_udpListener.Start();
		ct.Register(_udpListener.Stop);

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
				if (question.Type == ResourceType.PTR)
				{
					if (question.Name == "1.0.0.127.in-addr.arpa") // query for PTR record
					{
						message.QR = true;
						message.AA = true;
						message.RA = false;
						message.AnswerCount++;
						message.Answers.Add(
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
					}
				}
				else if (_resolvers.FirstOrDefault(x => x.TryGetZone(
					                                   question.Name,
					                                   out zone
				                                   )
				         ) !=
				         null)
				{
					var qName = question.Name.Replace($".{zone.Suffix}", "").Replace($"{zone.Suffix}", "");
					message.QR    = true;
					//message.AA    = true;
					message.RA    = true;//false;
					message.RCode = (byte)RCode.NOERROR;
					var zoneRecords = question.Type switch
					{
						ResourceType.ANY => zone.Records.Where(zr => zr.Host.Equals(qName)).ToList(),
						ResourceType.A => zone.Records.Where(zr => zr.Type is ResourceType.A or ResourceType.CNAME && zr.Host.Equals(qName)).ToList(),
						ResourceType.AAAA => zone.Records.Where(zr => zr.Type is ResourceType.AAAA or ResourceType.CNAME && zr.Host.Equals(qName)).ToList(),
						_ => zone.Records.Where(zr => zr.Type == question.Type && zr.Host.Equals(qName)).ToList(),
					};

					HandleRecords(zoneRecords, question, message, zone, remoteEndPoint);
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
							         Name  = question.Name,
							         Class = zoneRecord.Class,
							         Type  = zoneRecord.Type,
							         TTL   = 10,
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
							         Name  = question.Name,
							         Class = zoneRecord.Class,
							         Type  = zoneRecord.Type,
							         TTL   = 10,
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
							using PooledMemoryStream pms        = BufferPool.RentMemoryStream();
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
						Name  = question.Name,
						Class = zoneRecord.Class,
						Type  = zoneRecord.Type,
						TTL   = 10,
						RData = new SOARData
						{
							PrimaryNameServer               = zoneRecord.Addresses.Count> 1? zoneRecord.Addresses[0]:Environment.MachineName,
							ResponsibleAuthoritativeMailbox = zoneRecord.Addresses.Count> 1? zoneRecord.Addresses[1]:zoneRecord.Addresses[0],
							Serial                          = zone.Serial,
							ExpirationLimit                 = 86400,
							RetryInterval                   = 300,
							RefreshInterval                 = 300,
							MinimumTTL                      = 300,
						},
					};
					soaAnswer.TTL = (soaAnswer.RData as SOARData).MinimumTTL;

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
			var dnsServers        = adapterProperties.DnsAddresses;

			foreach (var dns in dnsServers)
			{
				logger.LogInformation("Discovered DNS: {Dns}", dns);

				yield return dns;
			}
		}
	}
}