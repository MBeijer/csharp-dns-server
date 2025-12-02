using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;
using Dns.Models;

namespace Dns.ZoneProvider;

public abstract class BaseZoneProvider(IDnsResolver resolver) : IZoneProvider, IDisposable
{
	private readonly List<IObserver<List<Zone>>> _observers = [];
	protected        Zone                        Zone { get; } = new() { Suffix = "", Serial = 0 };

	public abstract void Dispose();

	public IDnsResolver Resolver => resolver;

	public virtual void Initialize(ZoneOptions zoneOptions) => resolver.SubscribeTo(this);

	public IDisposable Subscribe(IObserver<List<Zone>> observer)
	{
		_observers.Add(observer);
		return new Subscription(this, observer);
	}

	public abstract void Start(CancellationToken ct);

	private void Unsubscribe(IObserver<List<Zone>> observer) => _observers.Remove(observer);

	/// <summary>Publish zone to all subscribers</summary>
	/// <param name="zone"></param>
	protected void Notify(List<Zone> zone)
	{
		var remainingRetries = 3;

		while (remainingRetries > 0)
		{
			var result = Parallel.ForEach(_observers, observer => observer.OnNext(zone));
			if (result.IsCompleted)
				break;
			remainingRetries--;
		}
	}

	/// <summary>Subscription memento for IObservable interface</summary>
	public class Subscription(BaseZoneProvider provider, IObserver<List<Zone>> observer) : IDisposable
	{
		public void Dispose() => provider.Unsubscribe(observer);
	}
}