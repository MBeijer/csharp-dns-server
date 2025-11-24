using System;
using System.Collections.Generic;
using System.Threading;
using Dns.Config;
using Dns.Contracts;
using Dns.Models;

namespace Dns.ZoneProvider;

public interface IZoneProvider : IObservable<List<Zone>>
{
	public void  Initialize(ZoneOptions zoneOptions);
	public void  Start(CancellationToken ct);
	IDnsResolver Resolver { get; }
}