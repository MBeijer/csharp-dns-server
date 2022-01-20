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
using System.Threading;
using Dns.Contracts;

namespace Dns
{
    public class SmartZoneResolver : IObserver<Zone>, IDnsResolver
    {
        private long _hits;
        private long _misses;
        private long _queries;
        private IDisposable _subscription;
        private Zone _zone;
        private Dictionary<string, IAddressDispenser> _zoneMap;
        private DateTime _zoneReload = DateTime.MinValue;

        public string GetZoneName() => Zone?.Suffix;

        public uint GetZoneSerial() => _zone.Serial;

        public Zone Zone
        {
            get => _zone;
            set
            {
                _zone = value ?? throw new ArgumentNullException(nameof(value));
                _zoneReload = DateTime.Now;
                _zoneMap = _zone.ToDictionary(GenerateKey, zoneRecord => new SmartAddressDispenser(zoneRecord) as IAddressDispenser, StringComparer.CurrentCultureIgnoreCase);
                Console.WriteLine("Zone reloaded");
            }
        }

        public DateTime LastZoneReload => _zoneReload;

        void IObserver<Zone>.OnCompleted() => throw new NotImplementedException();

        void IObserver<Zone>.OnError(Exception error) => throw new NotImplementedException();

        void IObserver<Zone>.OnNext(Zone value) => Zone = value;

        public void DumpHtml(TextWriter writer)
        {
            writer.WriteLine("Type:{0}<br/>", GetType().Name);
            writer.WriteLine("Queries:{0}<br/>", _queries);
            writer.WriteLine("Hits:{0}<br/>", _hits);
            writer.WriteLine("Misses:{0}<br/>", _misses);

            writer.WriteLine("<table>");
            writer.WriteLine("<tr><td>Key</td><td>Value</td></tr>");
            foreach (string key in _zoneMap.Keys)
            {
                writer.WriteLine("<tr><td>");
                writer.WriteLine(key);
                writer.WriteLine("</td><td>");
                _zoneMap[key].DumpHtml(writer);
                writer.WriteLine("</td></tr>");
            }
            writer.WriteLine("</table>");
        }

        public bool TryGetHostEntry(string hostName, ResourceClass resClass, ResourceType resType, out IPHostEntry entry)
        {
            if (hostName == null) throw new ArgumentNullException("hostName");
            if (hostName.Length > 126) throw new ArgumentOutOfRangeException("hostName");

            entry = null;

            Interlocked.Increment(ref _queries);

            // fail fasts
            if (!IsZoneLoaded()) return false;
            if (!hostName.EndsWith(_zone.Suffix)) return false;

            // lookup locally
            if (resType is ResourceType.ALL or ResourceType.ANY) resType = ResourceType.A;
            string key = GenerateKey(hostName, resClass, resType);
            IAddressDispenser dispenser;
            if (_zoneMap.TryGetValue(key, out dispenser))
            {
                Interlocked.Increment(ref _hits);
                entry = new IPHostEntry {AddressList = dispenser.GetAddresses().ToArray(), Aliases = new string[] {}, HostName = hostName};
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

            _subscription = zoneProvider.Subscribe(this);
        }

        private static string GenerateKey(ZoneRecord record) => GenerateKey(record.Host, record.Class, record.Type);

        private static string GenerateKey(string host, ResourceClass resClass, ResourceType resType) => $"{host}|{resClass}|{resType}";
    }
}