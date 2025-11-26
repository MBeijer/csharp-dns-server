// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="IDnsResolver.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Models;

namespace Dns.Contracts;

/// <summary>Provides domain name resolver capabilities</summary>
public interface IDnsResolver : IObserver<List<Zone>>, IHtmlDump
{
	public void       SubscribeTo(IObservable<List<Zone>> zoneProvider);
	IEnumerable<Zone> GetZones();
	bool TryGetZone(string hostname, out Zone zone);
}