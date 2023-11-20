using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Dns.ZoneProvider.IPProbe
{
    internal class Target
    {
        internal          IPAddress         Address;
        internal          Strategy.Probe    ProbeFunction;
        internal          ushort            TimeoutMilliseconds;
        internal readonly List<ProbeResult> Results = new();

        public override int GetHashCode() => $"{Address}|{ProbeFunction}|{TimeoutMilliseconds}".GetHashCode();

        internal bool IsAvailable
        {
            get
            {
                // Endpoint is available up-to last 3 results were successful
                return Results.TakeLast(3).All(r => r.Available);
            }
        }

        internal void AddResult(ProbeResult result)
        {
            Results.Add(result);
            if (Results.Count > 10)
            {
                Results.RemoveAt(0);
            }
        }


        internal class Comparer : IEqualityComparer<Target>
        {
            public bool Equals(Target x, Target y)
            {
                //Check whether the objects are the same object. 
                if (x.Equals(y)) return true;

                return x.GetHashCode() == y.GetHashCode();

            }

            public int GetHashCode(Target obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}