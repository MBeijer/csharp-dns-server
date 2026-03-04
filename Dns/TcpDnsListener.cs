using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Dns;

public delegate Task<byte[]> OnTcpRequestHandler(byte[] buffer, int length, EndPoint remoteEndPoint);

public class TcpDnsListener
{
	private readonly Lock                    _syncRoot = new();
	private          CancellationTokenSource _cts;
	private          TcpListener             _listener;
	private          Task                    _acceptLoopTask;

	public OnTcpRequestHandler OnRequest;

	public EndPoint LocalEndPoint => _listener?.LocalEndpoint;

	public void Initialize(ushort port = 53)
	{
		if (_listener != null) throw new InvalidOperationException("Listener already initialized.");

		_listener = new(IPAddress.Any, port);
	}

	public void Start()
	{
		if (_listener == null) throw new InvalidOperationException("Call Initialize before Start.");

		lock (_syncRoot)
		{
			if (_cts != null) throw new InvalidOperationException("TCP listener already started.");

			_listener.Start();
			_cts            = new();
			_acceptLoopTask = AcceptLoopAsync(_cts.Token);
		}
	}

	public void Stop()
	{
		CancellationTokenSource cts;
		Task                    acceptLoop;

		lock (_syncRoot)
		{
			if (_cts == null) return;

			cts             = _cts;
			acceptLoop      = _acceptLoopTask;
			_cts            = null;
			_acceptLoopTask = null;
		}

		cts.Cancel();
		_listener?.Stop();

		if (acceptLoop != null)
			try
			{
				acceptLoop.Wait();
			}
			catch (AggregateException ex)
			{
				ex.Handle(inner => inner is OperationCanceledException || inner is ObjectDisposedException);
			}

		cts.Dispose();
	}

	private async Task AcceptLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (ObjectDisposedException)
			{
				if (ct.IsCancellationRequested) break;
				throw;
			}

			_ = Task.Run(() => HandleClientAsync(client, ct), ct);
		}
	}

	private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
	{
		using (client)
		{
			var remote = client.Client.RemoteEndPoint;
			var stream = client.GetStream();

			while (!ct.IsCancellationRequested)
			{
				var lengthBuffer = new byte[2];
				var lengthRead = await ReadExactAsync(stream, lengthBuffer, ct).ConfigureAwait(false);
				if (lengthRead == 0) return;
				if (lengthRead < 2) return;

				var messageLength = BinaryPrimitives.ReadUInt16BigEndian(lengthBuffer);
				if (messageLength == 0) return;

				var payload = new byte[messageLength];
				var read = await ReadExactAsync(stream, payload, ct).ConfigureAwait(false);
				if (read < messageLength) return;

				if (OnRequest == null) continue;

				var response = await OnRequest(payload, payload.Length, remote).ConfigureAwait(false);
				if (response == null) continue;

				var responsePrefix = new byte[2];
				BinaryPrimitives.WriteUInt16BigEndian(responsePrefix, (ushort)response.Length);
				await stream.WriteAsync(responsePrefix, ct).ConfigureAwait(false);
				await stream.WriteAsync(response, ct).ConfigureAwait(false);
			}
		}
	}

	private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
			if (read == 0) break;
			offset += read;
		}

		return offset;
	}
}
