// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="DnsCache.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using Dns.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace Dns;

public class DnsCache : IDnsCache
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    byte[] IDnsCache.Get(string key) => _cache.TryGetValue(key, out byte[] entry) ? entry : null;

    void IDnsCache.Set(string key, byte[] bytes, int ttlSeconds)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions().SetAbsoluteExpiration(DateTimeOffset.Now + TimeSpan.FromSeconds(ttlSeconds));
        _cache.Set(key, bytes, cacheEntryOptions);
    }
}
