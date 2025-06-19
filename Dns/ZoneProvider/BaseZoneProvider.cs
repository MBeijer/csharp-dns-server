using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;

namespace Dns.ZoneProvider;

public abstract class BaseZoneProvider(IDnsResolver resolver) : IZoneProvider, IDisposable
{
    internal       uint   Serial = 0;

    protected static string Zone { get; set; }

    public          IDnsResolver Resolver => resolver;

    public virtual void Initialize(ZoneOptions zoneOptions) => resolver.SubscribeTo(this);

    private readonly List<IObserver<Zone>> _observers = [];

    public IDisposable Subscribe(IObserver<Zone> observer)
    {
        _observers.Add(observer);
        return new Subscription(this, observer);
    }

    private void Unsubscribe(IObserver<Zone> observer) => _observers.Remove(observer);

    public abstract void Dispose();

    /// <summary>Subscription memento for IObservable interface</summary>
    public class Subscription(BaseZoneProvider provider, IObserver<Zone> observer) : IDisposable
    {
        public void Dispose() => provider.Unsubscribe(observer);
    }

    /// <summary>Publish zone to all subscribers</summary>
    /// <param name="zone"></param>
    protected void Notify(Zone zone)
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

    public abstract void         Start(CancellationToken ct);
    
}