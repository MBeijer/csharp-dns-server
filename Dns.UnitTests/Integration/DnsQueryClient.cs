// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="DnsQueryClient.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
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

	public async Task<DnsMessage> QueryAsync(
		string hostName,
		ResourceType resourceType = ResourceType.A,
		bool recursionDesired = false,
		CancellationToken cancellationToken = default
	)
	{
		if (string.IsNullOrWhiteSpace(hostName))
			throw new ArgumentException("A host name is required.", nameof(hostName));

		using var udpClient = new UdpClient(AddressFamily.InterNetwork);
		var queryMessage = CreateQuery(hostName, resourceType, recursionDesired);
		var payload      = SerializeMessage(queryMessage);
		await udpClient.SendAsync(payload, payload.Length, _endpoint).ConfigureAwait(false);

		var receiveTask = udpClient.ReceiveAsync();
		var timeoutTask = Task.Delay(_timeout, cancellationToken);

		var completed = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);
		if (completed != receiveTask)
		{
			if (timeoutTask.IsCanceled)
				throw new OperationCanceledException("DNS query was cancelled.", cancellationToken);

			throw new TimeoutException($"Timed out waiting for DNS response for {hostName}.");
		}

		var receiveResult = await receiveTask.ConfigureAwait(false);
		if (!DnsMessage.TryParse(receiveResult.Buffer, out var response))
			throw new InvalidDataException("Unable to parse DNS response.");

		if (response.QueryIdentifier != queryMessage.QueryIdentifier)
			throw new InvalidOperationException("Received DNS response with mismatched identifier.");

		return response;
	}

	public async Task<DnsMessage> QueryTcpAsync(
		string hostName,
		ResourceType resourceType,
		CancellationToken cancellationToken = default
	)
	{
		var queryMessage = CreateQuery(hostName, resourceType, recursionDesired: false);
		return await SendTcpMessageAsync(queryMessage, cancellationToken).ConfigureAwait(false);
	}

	public async Task<DnsMessage> SendUdpMessageAsync(DnsMessage message, CancellationToken cancellationToken = default)
	{
		var payload = SerializeMessage(message);
		using var udpClient = new UdpClient(AddressFamily.InterNetwork);
		await udpClient.SendAsync(payload, payload.Length, _endpoint).ConfigureAwait(false);

		var receiveTask = udpClient.ReceiveAsync();
		var timeoutTask = Task.Delay(_timeout, cancellationToken);
		var completed   = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);
		if (completed != receiveTask)
		{
			if (timeoutTask.IsCanceled)
				throw new OperationCanceledException("DNS query was cancelled.", cancellationToken);

			throw new TimeoutException("Timed out waiting for DNS response.");
		}

		var result = await receiveTask.ConfigureAwait(false);
		if (!DnsMessage.TryParse(result.Buffer, out var response))
			throw new InvalidDataException("Unable to parse DNS response.");

		return response;
	}

	public async Task<DnsMessage> SendTcpMessageAsync(DnsMessage message, CancellationToken cancellationToken = default)
	{
		var payload = SerializeMessage(message);

		using var client = new TcpClient(AddressFamily.InterNetwork);
		var connectTask = client.ConnectAsync(_endpoint.Address, _endpoint.Port);
		var timeoutTask = Task.Delay(_timeout, cancellationToken);
		var completed   = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
		if (completed != connectTask)
		{
			if (timeoutTask.IsCanceled)
				throw new OperationCanceledException("DNS query was cancelled.", cancellationToken);

			throw new TimeoutException("Timed out connecting to DNS TCP endpoint.");
		}

		await connectTask.ConfigureAwait(false);

		var stream = client.GetStream();
		var prefix = new byte[2];
		BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)payload.Length);
		await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
		await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);

		var responsePrefix = new byte[2];
		var prefixRead = await ReadExactAsync(stream, responsePrefix, cancellationToken).ConfigureAwait(false);
		if (prefixRead < 2) throw new InvalidDataException("Incomplete DNS TCP response prefix.");

		var responseLength = BinaryPrimitives.ReadUInt16BigEndian(responsePrefix);
		var responseBytes  = new byte[responseLength];
		var responseRead = await ReadExactAsync(stream, responseBytes, cancellationToken).ConfigureAwait(false);
		if (responseRead < responseLength) throw new InvalidDataException("Incomplete DNS TCP response payload.");

		if (!DnsMessage.TryParse(responseBytes, out var response))
			throw new InvalidDataException("Unable to parse DNS response.");

		if (response.QueryIdentifier != message.QueryIdentifier)
			throw new InvalidOperationException("Received DNS response with mismatched identifier.");

		return response;
	}

	private DnsMessage CreateQuery(string hostName, ResourceType resourceType, bool recursionDesired)
	{
		var message = new DnsMessage
		{
			QueryIdentifier = (ushort)Interlocked.Increment(ref _messageId),
			QuestionCount   = 1,
			RD              = recursionDesired,
		};
		message.Questions.Add(new(hostName, resourceType, ResourceClass.IN));
		return message;
	}

	private static byte[] SerializeMessage(DnsMessage message)
	{
		using var stream = new MemoryStream();
		message.WriteToStream(stream);
		return stream.ToArray();
	}

	private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
			                       .ConfigureAwait(false);
			if (read == 0) break;
			offset += read;
		}

		return offset;
	}
}
