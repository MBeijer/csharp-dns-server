// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="APZoneResolver.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Dns.Contracts;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Models;
using Dns.ZoneProvider;
using Microsoft.Extensions.Logging;

namespace Dns;

public class SmartZoneResolver(ILogger<SmartZoneResolver> logger) : IDnsResolver
{
	private long          _hits;
	private long          _misses;
	private IZoneProvider _provider;
	private long          _queries;
	private IDisposable   _subscription;
	private List<Zone>    _zones = [];

	private static JsonSerializerOptions SerializerOptions { get; } = new() { WriteIndented = true };

	private List<Zone> Zones
	{
		get => _zones;
		set
		{
			_zones         = value ?? throw new ArgumentNullException(nameof(value));
			LastZoneReload = DateTime.Now;
			logger.LogInformation("Zone reloaded: {Zones}", string.Join(',', _zones.Select(z => z.Suffix)));
		}
	}

	public DateTime LastZoneReload { get; private set; } = DateTime.MinValue;

	public IEnumerable<Zone> GetZones() => Zones;

	void IObserver<List<Zone>>.OnCompleted() => throw new NotImplementedException();

	void IObserver<List<Zone>>.OnError(Exception error) => throw new NotImplementedException();

	void IObserver<List<Zone>>.OnNext(List<Zone> value) => Zones = value;

	public void DumpHtml(TextWriter writer)
	{
		writer.WriteLine("Type:{0}<br/>", _provider.GetType().Name);
		writer.WriteLine("Queries:{0}<br/>", _queries);
		writer.WriteLine("Hits:{0}<br/>", _hits);
		writer.WriteLine("Misses:{0}<br/>", _misses);

		if (_zones == null) return;
	}

	public object GetObject() => _zones;

	public bool TryGetZone(string hostName, out Zone zone)
	{
		zone = null;
		ArgumentNullException.ThrowIfNull(hostName);
		if (hostName.Length > 126) throw new ArgumentOutOfRangeException(nameof(hostName));

		Interlocked.Increment(ref _queries);

		if (!AreZonesLoaded()) return false;

		zone = _zones.FirstOrDefault(zone => hostName.EndsWith(zone.Suffix));

		if (zone != null)
		{
			Interlocked.Increment(ref _hits);

			return true;
		}

		Interlocked.Increment(ref _misses);
		return false;
	}

	/// <summary>Subscribe to specified zone provider</summary>
	/// <param name="zoneProvider"></param>
	public void SubscribeTo(IObservable<List<Zone>> zoneProvider)
	{
		// release previous subscription
		if (_subscription != null)
		{
			_subscription.Dispose();
			_subscription = null;
		}

		if (zoneProvider is IZoneProvider provider)
			_provider = provider;

		_subscription = zoneProvider.Subscribe(this);
	}

	public bool AreZonesLoaded() => _zones.Count > 0;
}