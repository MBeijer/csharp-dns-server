using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dns.ZoneProvider
{
    public abstract class BaseZoneProvider : IObservable<Zone>, IDisposable
    {
        internal uint _serial = 0;
        private static string _zone;

        protected static string Zone
        {
            get => _zone;
            set => _zone = value;
        }

        public abstract void Initialize(IServiceProvider serviceCollection, IConfiguration config, string zoneName);

        private readonly List<IObserver<Zone>> _observers = new();

        public IDisposable Subscribe(IObserver<Zone> observer)
        {
            _observers.Add(observer);
            return new Subscription(this, observer);
        }

        private void Unsubscribe(IObserver<Zone> observer) => _observers.Remove(observer);

        public abstract void Dispose();

        /// <summary>Subscription memento for IObservable interface</summary>
        public class Subscription : IDisposable
        {
            private readonly IObserver<Zone> _observer;
            private readonly BaseZoneProvider _provider;

            public Subscription(BaseZoneProvider provider, IObserver<Zone> observer)
            {
                _provider = provider;
                _observer = observer;
            }

            void IDisposable.Dispose() => _provider.Unsubscribe(_observer);
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

        public abstract void Start(CancellationToken ct);

    }
}