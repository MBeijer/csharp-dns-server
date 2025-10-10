// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="APZoneResolver.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using Dns.Contracts;
using Dns.ZoneProvider;

namespace Dns;

public class SmartZoneResolver : IDnsResolver
{
    private long                                  _hits;
    private long                                  _misses;
    private long                                  _queries;
    private IDisposable                           _subscription;
    private Zone                                  _zone;
    private Dictionary<string, IAddressDispenser> _zoneMap;
    private IZoneProvider                         _provider;

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
    };

    public string GetZoneName() => Zone?.Suffix;

    public uint GetZoneSerial() => _zone.Serial;

    private Zone Zone
    {
        get => _zone;
        set
        {
            _zone = value ?? throw new ArgumentNullException(nameof(value));
            LastZoneReload = DateTime.Now;
            _zoneMap = _zone.Records.ToDictionary(GenerateKey, IAddressDispenser (zoneRecord) => new SmartAddressDispenser(zoneRecord), StringComparer.CurrentCultureIgnoreCase);
            Console.WriteLine("Zone reloaded");
        }
    }

    public DateTime LastZoneReload { get; private set; } = DateTime.MinValue;

    void IObserver<Zone>.OnCompleted() => throw new NotImplementedException();

    void IObserver<Zone>.OnError(Exception error) => throw new NotImplementedException();

    void IObserver<Zone>.OnNext(Zone value) => Zone = value;

    public void DumpHtml(TextWriter writer)
    {
        writer.WriteLine("Type:{0}<br/>", _provider.GetType().Name);
        writer.WriteLine("Queries:{0}<br/>", _queries);
        writer.WriteLine("Hits:{0}<br/>", _hits);
        writer.WriteLine("Misses:{0}<br/>", _misses);

        if (_zone == null) return;

        writer.WriteLine("<pre>");

        writer.WriteLine($"{_zone.Suffix}                               IN SOA          ns1.eevul.net. marlon.eevul.net. (\n                                                {GetZoneSerial()}      ; serial (d. adams)\n                                                1H              ; refresh\n                                                15M             ; retry\n                                                1W              ; expiry\n                                                1D )            ; minimum\n");
        foreach (var record in _zoneMap.Select(s => s.Value.ZoneRecord))
        {
            foreach (var ipAddress in record.Addresses)
            {
                writer.WriteLine($"{record.Host}\t{record.Class} {record.Type}\t{ipAddress}");
            }
        }
        writer.WriteLine("</pre>");
        writer.WriteLine("<pre>");
        writer.WriteLine(JsonSerializer.Serialize(_zone, SerializerOptions));
        writer.WriteLine("</pre>");

        writer.WriteLine("<table>");
        writer.WriteLine("<tr><td>Key</td><td>Value</td></tr>");
        foreach (var key in _zoneMap.Keys)
        {
            writer.WriteLine("<tr><td>");
            writer.WriteLine(key);
            writer.WriteLine("</td><td>");
            _zoneMap[key].DumpHtml(writer);
            writer.WriteLine("</td></tr>");
        }
        writer.WriteLine("</table>");
    }

    public object GetObject() => _zone;

    public bool TryGetHostEntry(string hostName, ResourceClass resClass, ResourceType resType, out IPHostEntry entry)
    {
        if (hostName == null) throw new ArgumentNullException(nameof(hostName));
        if (hostName.Length > 126) throw new ArgumentOutOfRangeException(nameof(hostName));

        entry = null;

        Interlocked.Increment(ref _queries);

        // fail fasts
        if (!IsZoneLoaded()) return false;
        if (!hostName.EndsWith(_zone.Suffix)) return false;

        // lookup locally
        if (resType is ResourceType.ALL or ResourceType.ANY) resType = ResourceType.A;
        var key = GenerateKey(hostName, resClass, resType);
        if (_zoneMap.TryGetValue(key, out var dispenser))
        {
            Interlocked.Increment(ref _hits);
            entry = new() {AddressList = dispenser.GetAddresses().ToArray(), Aliases = [], HostName = hostName};
            return true;
        }

        Interlocked.Increment(ref _misses);
        return false;
    }

    public bool IsZoneLoaded() => _zone != null;

    /// <summary>Subscribe to specified zone provider</summary>
    /// <param name="zoneProvider"></param>
    public void SubscribeTo(IObservable<Zone> zoneProvider)
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

    private static string GenerateKey(ZoneRecord record) => GenerateKey(record.Host, record.Class, record.Type);

    private static string GenerateKey(string host, ResourceClass resClass, ResourceType resType) => $"{host}|{resClass}|{resType}";
}