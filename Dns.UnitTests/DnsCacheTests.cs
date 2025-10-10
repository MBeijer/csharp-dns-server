using System.Linq;
using System.Text;
using Dns.Contracts;
using Xunit;

namespace Dns.UnitTests;

public class DnsCacheTests
{
    [Fact]
    public void Test1() {
        IDnsCache cache = new DnsCache();
        var invalidKeyResult = cache.Get("invalidTestKey");
        Assert.Null(invalidKeyResult);
    }

    [Fact]
    public void Test2() {
        IDnsCache cache = new DnsCache();

        const string key = "sampleCacheKey";
        var data = Encoding.ASCII.GetBytes("test");
        const int ttl = 10;

        cache.Set(key, data, ttl);
        var result = cache.Get(key);

        Assert.True(data.SequenceEqual(result));

        var invalidKeyResult = cache.Get("invalidTestKey");
        Assert.Null(invalidKeyResult);
    }
}