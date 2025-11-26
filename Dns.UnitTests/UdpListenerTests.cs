// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="UdpListenerTests.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Dns.UnitTests;

public class UdpListenerTests
{
	[Fact]
	public async Task Stop_ReleasesPortAndHaltsProcessing()
	{
		var listener = new UdpListener();
		listener.Initialize(0);

		try
		{
			var firstPacket = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var invocations = 0;

			listener.OnRequest += (buffer, length, remote) =>
			{
				if (Interlocked.Increment(ref invocations) == 1) firstPacket.TrySetResult(true);
			};

			listener.Start();
			var port = ((IPEndPoint)listener.LocalEndPoint).Port;

			using (var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
			{
				await client.SendAsync([0x1], 1, new(IPAddress.Loopback, port));
			}

			await firstPacket.Task.WaitAsync(TimeSpan.FromSeconds(5));

			listener.Stop();

			var received = Volatile.Read(ref invocations);
			Assert.Equal(1, received);

			using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
			{
				probe.Bind(new IPEndPoint(IPAddress.Loopback, port));
			}
		}
		finally
		{
			listener.Stop();
		}
	}

	[Fact]
	public async Task CapturesRemoteEndpointPerPacket()
	{
		var listener = new UdpListener();
		listener.Initialize(0);

		try
		{
			var captured   = new List<IPEndPoint>();
			var gate       = new object();
			var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			listener.OnRequest += (buffer, length, remote) =>
			{
				lock (gate)
				{
					if (remote is IPEndPoint ip)
					{
						captured.Add(new(ip.Address, ip.Port));
						if (captured.Count == 2) completion.TrySetResult(true);
					}
				}
			};

			listener.Start();
			var port = ((IPEndPoint)listener.LocalEndPoint).Port;

			using (var client1 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
			using (var client2 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
			{
				var target = new IPEndPoint(IPAddress.Loopback, port);

				await Task.WhenAll(client1.SendAsync([0x1], 1, target), client2.SendAsync([0x2], 1, target));

				await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

				var expectedPorts = new[]
					{
						((IPEndPoint)client1.Client.LocalEndPoint).Port,
						((IPEndPoint)client2.Client.LocalEndPoint).Port,
					}.OrderBy(p => p)
					 .ToArray();

				var actualPorts = captured.Select(ep => ep.Port).OrderBy(p => p).ToArray();
				Assert.Equal(expectedPorts, actualPorts);
			}
		}
		finally
		{
			listener.Stop();
		}
	}
}