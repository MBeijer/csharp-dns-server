// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="UdpListener.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Dns.Extensions;

namespace Dns;

    /// <summary>
    /// Handler for incoming DNS requests.
    /// </summary>
    /// <param name="buffer">The received data buffer.</param>
    /// <param name="length">The number of valid bytes in the buffer.</param>
    /// <param name="remoteEndPoint">The remote endpoint that sent the request.</param>
public delegate void OnRequestHandler(byte[] buffer, int length, EndPoint remoteEndPoint);

public class UdpListener
{
	public OnRequestHandler OnRequest;

	private readonly Lock                    _syncRoot = new();
	private          Socket                  _listener;
	private          CancellationTokenSource _cts;
	private          Task                    _receiveLoopTask;

	public EndPoint LocalEndPoint => _listener?.LocalEndPoint;

	public void Initialize(ushort port = 53)
	{
		if (_listener != null)
		{
			throw new InvalidOperationException("Listener already initialized.");
		}

		_listener = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		var ep = new IPEndPoint(IPAddress.Any, port);
		_listener.Bind(ep);
	}

	public void Start()
	{
		if (_listener == null)
		{
			throw new InvalidOperationException("Call Initialize before Start.");
		}

		lock (_syncRoot)
		{
			if (_cts != null)
			{
				throw new InvalidOperationException("UDP listener already started.");
			}

			_cts             = new();
			_receiveLoopTask = ReceiveLoopAsync(_cts.Token);
		}
	}

	public void Stop()
	{
		CancellationTokenSource cts;
		Task                    receiveLoop;

		lock (_syncRoot)
		{
			if (_cts == null)
			{
				return;
			}

			cts              = _cts;
			receiveLoop      = _receiveLoopTask;
			_cts             = null;
			_receiveLoopTask = null;
		}

		cts.Cancel();

		_listener?.Close();

		if (receiveLoop != null)
		{
			try
			{
				receiveLoop.Wait();
			}
			catch (AggregateException ex)
			{
				ex.Handle(inner => inner is OperationCanceledException || inner is ObjectDisposedException);
			}
		}

		cts.Dispose();
	}

	public async void SendToAsync(SocketAsyncEventArgs args)
	{
		if (_listener == null)
		{
			throw new InvalidOperationException("Listener is not initialized.");
		}

		var awaitable = new SocketAwaitable(args);
		await _listener.SendToAsync(awaitable);
	}

	private async Task ReceiveLoopAsync(CancellationToken ct)
	{
		var listener = _listener;
		if (listener == null)
		{
			return;
		}

            var args = new SocketAsyncEventArgs();
            args.SetBuffer(new byte[0x1000], 0, 0x1000);
            var awaitable = new SocketAwaitable(args);

		try
		{
			while (!ct.IsCancellationRequested)
			{
				args.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

				try
				{
					await listener.ReceiveFromAsync(awaitable);
					var bytesRead = args.BytesTransferred;
					if (bytesRead <= 0)
					{
						continue;
					}

					var payload = new byte[bytesRead];
					Buffer.BlockCopy(args.Buffer, 0, payload, 0, bytesRead);

					var remoteClone = CloneEndPoint(args.RemoteEndPoint);

					if (OnRequest != null)
					{
						_ = Task.Run(() => OnRequest(payload, bytesRead, remoteClone));
					}
					else
					{
						_ = Task.Run(() => ProcessReceiveFrom(remoteClone, bytesRead));
					}
				}
				catch (ObjectDisposedException)
				{
					if (ct.IsCancellationRequested)
					{
						break;
					}

					throw;
				}
				catch (SocketException ex)
				{
					if (ct.IsCancellationRequested &&
					    (ex.SocketErrorCode == SocketError.OperationAborted ||
					     ex.SocketErrorCode == SocketError.Interrupted))
					{
						break;
					}

					Console.WriteLine(ex.ToString());
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
				}
			}
		}
		finally
		{
			args.Dispose();
		}
	}

	private static EndPoint CloneEndPoint(EndPoint endpoint)
	{
		if (endpoint == null)
		{
			return null;
		}

		if (endpoint is IPEndPoint ip)
		{
			return new IPEndPoint(ip.Address, ip.Port);
		}

		var address = endpoint.Serialize();
		return endpoint.Create(address);
	}

	public void ProcessReceiveFrom(EndPoint remoteEndPoint, int bytesTransferred)
	{
		Console.WriteLine(remoteEndPoint?.ToString());
		Console.WriteLine(bytesTransferred);
	}
}

/// <summary>IO completion based socket await object</summary>
public sealed class SocketAwaitable : INotifyCompletion
{
	private static readonly Action SENTINEL = () => { };

	internal Action               m_continuation;
	internal SocketAsyncEventArgs m_eventArgs;
	internal bool                 m_wasCompleted;

	public SocketAwaitable(SocketAsyncEventArgs eventArgs)
	{
		m_eventArgs = eventArgs ?? throw new ArgumentNullException("eventArgs");
		eventArgs.Completed += delegate
		{
			var prev = m_continuation ?? Interlocked.CompareExchange(ref m_continuation, SENTINEL, null);
			prev?.Invoke();
		};
	}

	public bool IsCompleted => m_wasCompleted;

	public void OnCompleted(Action continuation)
	{
		if (m_continuation == SENTINEL ||
		    Interlocked.CompareExchange(ref m_continuation, continuation, null) == SENTINEL)
		{
			Task.Run(continuation);
		}
	}

	internal void Reset()
	{
		m_wasCompleted = false;
		m_continuation = null;
	}

	public SocketAwaitable GetAwaiter() => this;

	public void GetResult()
	{
		if (m_eventArgs.SocketError != SocketError.Success)
			throw new SocketException((int)m_eventArgs.SocketError);
	}
}