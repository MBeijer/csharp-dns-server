// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="DnsQueryClient.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Dns.Db.Models.EntityFramework.Enums;

namespace Dns.UnitTests.Integration;

internal sealed class DnsQueryClient(IPEndPoint endpoint, TimeSpan? timeout = null)
{
    private readonly IPEndPoint _endpoint  = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
    private readonly TimeSpan   _timeout   = timeout ?? TimeSpan.FromSeconds(5);
    private          int        _messageId = Environment.TickCount;

    public async Task<DnsMessage> QueryAsync(string hostName, ResourceType resourceType = ResourceType.A, bool recursionDesired = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            throw new ArgumentException("A host name is required.", nameof(hostName));
        }

        var queryMessage = CreateQuery(hostName, resourceType, recursionDesired);
        var payload      = SerializeMessage(queryMessage);

        using var udpClient = new UdpClient(AddressFamily.InterNetwork);
        await udpClient.SendAsync(payload, payload.Length, _endpoint).ConfigureAwait(false);

        var receiveTask = udpClient.ReceiveAsync();
        var timeoutTask = Task.Delay(_timeout, cancellationToken);

        var completed = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);
        if (completed != receiveTask)
        {
            if (timeoutTask.IsCanceled)
            {
                throw new OperationCanceledException("DNS query was cancelled.", cancellationToken);
            }

            throw new TimeoutException($"Timed out waiting for DNS response for {hostName}.");
        }

        var receiveResult = await receiveTask.ConfigureAwait(false);
        if (!DnsMessage.TryParse(receiveResult.Buffer, out var response))
        {
            throw new InvalidDataException("Unable to parse DNS response.");
        }

        if (response.QueryIdentifier != queryMessage.QueryIdentifier)
        {
            throw new InvalidOperationException("Received DNS response with mismatched identifier.");
        }

        return response;
    }

    private DnsMessage CreateQuery(string hostName, ResourceType resourceType, bool recursionDesired)
    {
        var message = new DnsMessage
        {
            QueryIdentifier = (ushort)Interlocked.Increment(ref _messageId), QuestionCount = 1, RD = recursionDesired,
        };
        message.Questions.Add(new (hostName, resourceType, ResourceClass.IN));
        return message;
    }

    private static byte[] SerializeMessage(DnsMessage message)
    {
        using var stream = new MemoryStream();
        message.WriteToStream(stream);
        return stream.ToArray();
    }
}