﻿// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="DnsServer.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using Dns.Contracts;
using Dns.RDataTypes;

namespace Dns;

internal class DnsServer : IHtmlDump
{
    private IPAddress[]  _defaultDns;
    private UdpListener  _udpListener; // listener for UDP53 traffic
    private IDnsResolver _resolver; // resolver for name entries
    private long         _requests;
    private long         _responses;
    private long         _nacks;

    private Dictionary<string, EndPoint> _requestResponseMap = new();

    private ReaderWriterLockSlim _requestResponseMapLock = new();

    private ushort port;

    internal DnsServer(ushort port)
    {
        this.port = port;
    }

    /// <summary>Initialize server with specified domain name resolver</summary>
    /// <param name="resolver"></param>
    public void Initialize(IDnsResolver resolver)
    {
        _resolver = resolver;

        _udpListener = new();

        _udpListener.Initialize(port);
        _udpListener.OnRequest += ProcessUdpRequest;

        _defaultDns = GetDefaultDNS().ToArray();
    }

    /// <summary>Start DNS listener</summary>
    public void Start(CancellationToken ct)
    {
        _udpListener.Start();
        ct.Register(_udpListener.Stop);
    }

    /// <summary>Process UDP Request</summary>
    /// <param name="args"></param>
    private void ProcessUdpRequest(byte[] buffer, EndPoint remoteEndPoint)
    {
        if (!DnsProtocol.TryParse(buffer, out var message))
        {
            // TODO log bad message
            Console.WriteLine("unable to parse message");
            return;
        }

        Interlocked.Increment(ref _requests);

        if (message.IsQuery())
        {
            if (message.Questions.Count > 0)
            {
                foreach (var question in message.Questions)
                {
                    Console.WriteLine(
                        "{0} asked for {1} {2} {3}",
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
                                    Name = question.Name,
                                    Class = ResourceClass.IN,
                                    Type = ResourceType.PTR,
                                    TTL = 3600,
                                    DataLength = 0xB,
                                    RData = new DomainNamePointRData { Name = "localhost" },
                                }
                            );
                        }
                    }
                    else if (_resolver.TryGetHostEntry(
                                 question.Name,
                                 question.Class,
                                 question.Type,
                                 out var entry
                             )) // Right zone, hostname/machine function does exist
                    {
                        message.QR = true;
                        message.AA = true;
                        message.RA = false;
                        message.RCode = (byte)RCode.NOERROR;
                        foreach (var address in entry.AddressList)
                        {
                            message.AnswerCount++;
                            message.Answers.Add(
                                new()
                                {
                                    Name = question.Name,
                                    Class = ResourceClass.IN,
                                    Type = ResourceType.A,
                                    TTL = 10,
                                    RData = new ANameRData { Address = address },
                                }
                            );
                        }
                    }
                    else if
                        (question.Name.EndsWith(
                                _resolver.GetZoneName()
                            )) // Right zone, but the hostname/machine function doesn't exist
                    {
                        message.QR = true;
                        message.AA = true;
                        message.RA = false;
                        message.RCode = (byte)RCode.NXDOMAIN;
                        message.AnswerCount = 0;
                        message.Answers.Clear();

                        var soaResourceData = new StatementOfAuthorityRData
                        {
                            PrimaryNameServer = Environment.MachineName,
                            ResponsibleAuthoritativeMailbox = "stephbu." + Environment.MachineName,
                            Serial = _resolver.GetZoneSerial(),
                            ExpirationLimit = 86400,
                            RetryInterval = 300,
                            RefreshInterval = 300,
                            MinimumTTL = 300,
                        };
                        var soaResourceRecord = new ResourceRecord
                        {
                            Class = ResourceClass.IN,
                            Type = ResourceType.SOA,
                            TTL = 300,
                            RData = soaResourceData,
                        };
                        message.NameServerCount++;
                        message.Authorities.Add(soaResourceRecord);
                    }
                    //
                    else // Referral to regular DC DNS servers
                    {
                        // store current IP address and Query ID.
                        try
                        {
                            var key = GetKeyName(message);
                            _requestResponseMapLock.EnterWriteLock();
                            _requestResponseMap.Add(key, remoteEndPoint);
                        }
                        finally
                        {
                            _requestResponseMapLock.ExitWriteLock();
                        }
                    }

                    using MemoryStream responseStream = new(512);

                    message.WriteToStream(responseStream);
                    if (message.IsQuery())
                    {
                        // send to upstream DNS servers
                        foreach (var dnsServer in _defaultDns)
                        {
                            SendUdp(
                                responseStream.GetBuffer(),
                                0,
                                (int)responseStream.Position,
                                new IPEndPoint(dnsServer, 53)
                            );
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref _responses);
                        SendUdp(responseStream.GetBuffer(), 0, (int)responseStream.Position, remoteEndPoint);
                    }
                }
            }
        }
        else
        {
            // message is response to a delegated query
            var key = GetKeyName(message);
            try
            {
                _requestResponseMapLock.EnterUpgradeableReadLock();

                if (_requestResponseMap.TryGetValue(key, out var ep))
                {
                    // first test establishes presence
                    try
                    {
                        _requestResponseMapLock.EnterWriteLock();
                        // second test within lock means exclusive access
                        if (!_requestResponseMap.TryGetValue(key, out ep)) return;

                        using (MemoryStream responseStream = new(512))
                        {
                            message.WriteToStream(responseStream);
                            Interlocked.Increment(ref _responses);

                            Console.WriteLine("{0} answered {1} {2} {3} to {4}", remoteEndPoint, message.Questions[0].Name, message.Questions[0].Class, message.Questions[0].Type, ep);

                            SendUdp(responseStream.GetBuffer(), 0, (int)responseStream.Position, ep);
                        }
                        _requestResponseMap.Remove(key);

                    }
                    finally
                    {
                        _requestResponseMapLock.ExitWriteLock();
                    }
                }
                else
                {
                    Interlocked.Increment(ref _nacks);
                }
            }
            finally
            {
                _requestResponseMapLock.ExitUpgradeableReadLock();
            }
        }
    }

    private static string GetKeyName(DnsMessage message) => message.QuestionCount > 0 ? $"{message.QueryIdentifier}|{message.Questions[0].Class}|{message.Questions[0].Type}|{message.Questions[0].Name}" : message.QueryIdentifier.ToString();

    /// <summary>Send UDP response via UDP listener socket</summary>
    /// <param name="bytes"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <param name="remoteEndpoint"></param>
    private void SendUdp(byte[] bytes, int offset, int count, EndPoint remoteEndpoint)
    {
        SocketAsyncEventArgs args = new();
        args.RemoteEndPoint = remoteEndpoint;
        args.SetBuffer(bytes, offset, count);

        _udpListener.SendToAsync(args);
    }

    /// <summary>Returns list of manual or DHCP specified DNS addresses</summary>
    /// <returns>List of configured DNS names</returns>
    // ReSharper disable once InconsistentNaming
    private static IEnumerable<IPAddress> GetDefaultDNS()
    {
        var adapters  = NetworkInterface.GetAllNetworkInterfaces();
        foreach (var adapter in adapters)
        {

            var adapterProperties = adapter.GetIPProperties();
            var dnsServers = adapterProperties.DnsAddresses;

            foreach (var dns in dnsServers)
            {
                Console.WriteLine($"Discovered DNS: {dns}");

                yield return dns;
            }

        }
    }

    public void DumpHtml(TextWriter writer)
    {
        writer.WriteLine("DNS Server Status<br/>");
        writer.Write("Default Nameservers:");
        foreach (var dns in _defaultDns)
        {
            writer.WriteLine(dns);
        }
        writer.WriteLine("DNS Server Status<br/>");
    }
}