using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Dns.Config;
using Dns.Models;
using Dns.ZoneProvider;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Dns.UnitTests;

public class SmartZoneResolverTests
{
	[Fact]
	public void TryGetZone_WhenNoZonesLoaded_ReturnsFalse()
	{
		var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());

		var found = resolver.TryGetZone("www.example.com", out var zone);

		Assert.False(found);
		Assert.Null(zone);
		Assert.False(resolver.AreZonesLoaded());
	}

	[Fact]
	public void TryGetZone_WithNullHost_Throws()
	{
		var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());

		Assert.Throws<ArgumentNullException>(() => resolver.TryGetZone(null, out _));
	}

	[Fact]
	public void TryGetZone_WithHostNameTooLong_Throws()
	{
		var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var hostName = new string('a', 127);

		Assert.Throws<ArgumentOutOfRangeException>(() => resolver.TryGetZone(hostName, out _));
	}

	[Fact]
	public void ObserverOnNext_LoadsZonesAndUpdatesReloadTime()
	{
		var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var observer = (IObserver<List<Zone>>)resolver;
		var before = DateTime.Now;

		observer.OnNext(
			[
				new Zone { Suffix = "example.com", Serial = 1 },
			]
		);

		Assert.True(resolver.AreZonesLoaded());
		Assert.True(resolver.LastZoneReload >= before);
		Assert.Single(resolver.GetZones());
	}

	[Fact]
	public void TryGetZone_WhenZoneExists_ReturnsHit()
	{
		var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var observer = (IObserver<List<Zone>>)resolver;
		observer.OnNext(
			[
				new Zone { Suffix = "example.com", Serial = 1 },
			]
		);

		var found = resolver.TryGetZone("api.example.com", out var zone);

		Assert.True(found);
		Assert.NotNull(zone);
		Assert.Equal("example.com", zone.Suffix);
	}

	[Fact]
	public void TryGetZone_WhenZoneDoesNotExist_ReturnsMiss()
	{
		var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var observer = (IObserver<List<Zone>>)resolver;
		observer.OnNext(
			[
				new Zone { Suffix = "example.com", Serial = 1 },
			]
		);

		var found = resolver.TryGetZone("api.other.net", out var zone);

		Assert.False(found);
		Assert.Null(zone);
	}

	[Fact]
	public void SubscribeTo_DisposesPreviousSubscription()
	{
		var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var firstProvider = new FakeZoneProvider();
		var secondProvider = new FakeZoneProvider();

		resolver.SubscribeTo(firstProvider);
		resolver.SubscribeTo(secondProvider);

		Assert.True(firstProvider.LastSubscriptionDisposed);
	}

	[Fact]
	public void SubscribeTo_ThenPublish_DumpHtmlIncludesProviderAndStats()
	{
		var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var provider = new FakeZoneProvider();
		resolver.SubscribeTo(provider);

		provider.Publish(
			[
				new Zone { Suffix = "example.com", Serial = 1 },
			]
		);

		resolver.TryGetZone("a.example.com", out _);
		resolver.TryGetZone("a.invalid", out _);

		using var writer = new StringWriter();
		resolver.DumpHtml(writer);
		var html = writer.ToString();

		Assert.Contains("Type:FakeZoneProvider", html);
		Assert.Contains("Queries:2", html);
		Assert.Contains("Hits:1", html);
		Assert.Contains("Misses:1", html);
		Assert.Same(provider.PublishedZones, resolver.GetObject());
	}

	[Fact]
	public void ObserverOnCompleted_ThrowsNotImplemented()
	{
		var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var observer = (IObserver<List<Zone>>)resolver;

		Assert.Throws<NotImplementedException>(() => observer.OnCompleted());
	}

	[Fact]
	public void ObserverOnError_ThrowsNotImplemented()
	{
		var resolver = new SmartZoneResolver(new FakeLogger<SmartZoneResolver>());
		var observer = (IObserver<List<Zone>>)resolver;

		Assert.Throws<NotImplementedException>(() => observer.OnError(new Exception("boom")));
	}

	private sealed class FakeZoneProvider : IZoneProvider
	{
		private IObserver<List<Zone>> _observer;

		public bool LastSubscriptionDisposed { get; private set; }

		public List<Zone> PublishedZones { get; private set; }

		public Dns.Contracts.IDnsResolver Resolver => null;

		public IDisposable Subscribe(IObserver<List<Zone>> observer)
		{
			_observer = observer;
			return new FakeSubscription(() => LastSubscriptionDisposed = true);
		}

		public void Publish(List<Zone> zones)
		{
			PublishedZones = zones;
			_observer?.OnNext(zones);
		}

		public void Initialize(ZoneOptions zoneOptions)
		{
		}

		public void Start(CancellationToken ct)
		{
		}

		private sealed class FakeSubscription(Action onDispose) : IDisposable
		{
			public void Dispose() => onDispose();
		}
	}
}