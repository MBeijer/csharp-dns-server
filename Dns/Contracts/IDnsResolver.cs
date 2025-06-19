// // //------------------------------------------------------------------------------------------------- 
// // // <copyright file="IDnsResolver.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Net;

namespace Dns.Contracts;

/// <summary>Provides domain name resolver capabilities</summary>
public interface IDnsResolver : IObserver<Zone>, IHtmlDump
{
    public void SubscribeTo(IObservable<Zone> zoneProvider);
    string      GetZoneName();

    uint GetZoneSerial();

    bool TryGetHostEntry(string hostname, ResourceClass resClass, ResourceType resType, out IPHostEntry entry);
}