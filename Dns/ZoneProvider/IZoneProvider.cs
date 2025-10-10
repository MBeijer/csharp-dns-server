using System;
using System.Threading;
using Dns.Config;
using Dns.Contracts;

namespace Dns.ZoneProvider;

public interface IZoneProvider : IObservable<Zone>
{
	public void  Initialize(ZoneOptions zoneOptions);
	public void  Start(CancellationToken ct);
	IDnsResolver Resolver { get; }
}