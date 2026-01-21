// //-------------------------------------------------------------------------------------------------
// // <copyright file="CacheLookupBenchmarks.cs" company="stephbu">
// // Copyright (c) Steve Butler. All rights reserved.
// // </copyright>
// //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Dns.Db.Models.EntityFramework.Enums;

namespace Dns.Benchmarks;

/// <summary>
///     Phase 4 Benchmarks: Cache lookup performance comparing string keys vs struct keys.
///     Measures key creation allocation and dictionary lookup performance.
/// </summary>
[MemoryDiagnoser]
public class CacheLookupBenchmarks
{
	// Test data
	private const string        TestHostname = "www.msn.com.redmond.corp.microsoft.com";
	private const ushort        TestQueryId  = 0xD303;
	private const ResourceClass TestClass    = ResourceClass.IN;
	private const ResourceType  TestType     = ResourceType.A;

	// Value for lookups
	private static readonly object        DummyValue = new();
	private                 DnsRequestKey _precomputedRequestKey;
	private                 string        _precomputedRequestStringKey;
	private                 string        _precomputedStringKey;

	// Pre-computed keys
	private DnsZoneLookupKey _precomputedZoneKey;

	// Request key dictionaries (simulating DnsServer request map)
	private Dictionary<string, object>                  _requestStringDict;
	private ConcurrentDictionary<DnsRequestKey, object> _requestStructDict;
	private ConcurrentDictionary<string, object>        _stringConcurrentDict;

	// Legacy string-keyed dictionaries
	private Dictionary<string, object>                     _stringDictionary;
	private ConcurrentDictionary<DnsZoneLookupKey, object> _structConcurrentDict;

	// Optimized struct-keyed dictionaries
	private Dictionary<DnsZoneLookupKey, object>       _structDictionary;
	private FrozenDictionary<DnsZoneLookupKey, object> _structFrozenDict;

	[GlobalSetup]
	public void Setup()
	{
		// Create pre-computed keys
		_precomputedZoneKey          = new(TestHostname, TestClass, TestType);
		_precomputedRequestKey       = new(TestQueryId, TestClass, TestType, TestHostname);
		_precomputedStringKey        = GenerateStringKey(TestHostname, TestClass, TestType);
		_precomputedRequestStringKey = GenerateRequestStringKey(TestQueryId, TestClass, TestType, TestHostname);

		// Setup legacy dictionaries
		_stringDictionary                            = new(StringComparer.OrdinalIgnoreCase) { [_precomputedStringKey] = DummyValue };
		_stringConcurrentDict                        = new(StringComparer.OrdinalIgnoreCase);
		_stringConcurrentDict[_precomputedStringKey] = DummyValue;

		// Setup optimized dictionaries
		_structDictionary                          = new() { [_precomputedZoneKey] = DummyValue };
		_structConcurrentDict                      = new();
		_structConcurrentDict[_precomputedZoneKey] = DummyValue;

		// Setup request dictionaries
		_requestStringDict                         = new() { [_precomputedRequestStringKey] = DummyValue };
		_requestStructDict                         = new();
		_requestStructDict[_precomputedRequestKey] = DummyValue;

		// Add more entries to simulate realistic dictionary size
		for (var i = 0; i < 1000; i++)
		{
			var host    = $"host{i}.example.com";
			var zoneKey = new DnsZoneLookupKey(host, TestClass, TestType);
			var strKey  = GenerateStringKey(host, TestClass, TestType);

			_stringDictionary[strKey]      = DummyValue;
			_stringConcurrentDict[strKey]  = DummyValue;
			_structDictionary[zoneKey]     = DummyValue;
			_structConcurrentDict[zoneKey] = DummyValue;
		}

		// Create FrozenDictionary from struct dictionary (optimal for read-only scenarios)
		_structFrozenDict = _structDictionary.ToFrozenDictionary();
	}

	/// <summary>Legacy string key generation (string.Format allocation)</summary>
	private static string GenerateStringKey(string host, ResourceClass resClass, ResourceType resType) =>
		string.Format("{0}|{1}|{2}", host, resClass, resType);

	/// <summary>Legacy request key generation (string.Format allocation)</summary>
	private static string GenerateRequestStringKey(
		ushort queryId,
		ResourceClass resClass,
		ResourceType resType,
		string host
	) =>
		string.Format("{0}|{1}|{2}|{3}", queryId, resClass, resType, host);

	// ========== Key Creation Benchmarks ==========

	[Benchmark(Baseline = true, Description = "Zone Key: Legacy string.Format")]
	public string CreateZoneKey_Legacy() => GenerateStringKey(TestHostname, TestClass, TestType);

	[Benchmark(Description = "Zone Key: Struct constructor")]
	public DnsZoneLookupKey CreateZoneKey_Struct() => new(TestHostname, TestClass, TestType);

	[Benchmark(Description = "Request Key: Legacy string.Format")]
	public string CreateRequestKey_Legacy() => GenerateRequestStringKey(TestQueryId, TestClass, TestType, TestHostname);

	[Benchmark(Description = "Request Key: Struct constructor")]
	public DnsRequestKey CreateRequestKey_Struct() => new(TestQueryId, TestClass, TestType, TestHostname);

	// ========== Zone Lookup Benchmarks ==========

	[Benchmark(Description = "Zone Lookup: Dictionary<string>")]
	public bool ZoneLookup_Dictionary_String()
	{
		var key = GenerateStringKey(TestHostname, TestClass, TestType);
		return _stringDictionary.TryGetValue(key, out _);
	}

	[Benchmark(Description = "Zone Lookup: Dictionary<struct>")]
	public bool ZoneLookup_Dictionary_Struct()
	{
		var key = new DnsZoneLookupKey(TestHostname, TestClass, TestType);
		return _structDictionary.TryGetValue(key, out _);
	}

	[Benchmark(Description = "Zone Lookup: ConcurrentDict<string>")]
	public bool ZoneLookup_ConcurrentDict_String()
	{
		var key = GenerateStringKey(TestHostname, TestClass, TestType);
		return _stringConcurrentDict.TryGetValue(key, out _);
	}

	[Benchmark(Description = "Zone Lookup: ConcurrentDict<struct>")]
	public bool ZoneLookup_ConcurrentDict_Struct()
	{
		var key = new DnsZoneLookupKey(TestHostname, TestClass, TestType);
		return _structConcurrentDict.TryGetValue(key, out _);
	}

	[Benchmark(Description = "Zone Lookup: FrozenDict<struct>")]
	public bool ZoneLookup_FrozenDict_Struct()
	{
		var key = new DnsZoneLookupKey(TestHostname, TestClass, TestType);
		return _structFrozenDict.TryGetValue(key, out _);
	}

	// ========== Request Map Benchmarks ==========

	[Benchmark(Description = "Request Map: Dictionary<string>")]
	public bool RequestLookup_Dictionary_String()
	{
		var key = GenerateRequestStringKey(TestQueryId, TestClass, TestType, TestHostname);
		return _requestStringDict.TryGetValue(key, out _);
	}

	[Benchmark(Description = "Request Map: ConcurrentDict<struct>")]
	public bool RequestLookup_ConcurrentDict_Struct()
	{
		var key = new DnsRequestKey(TestQueryId, TestClass, TestType, TestHostname);
		return _requestStructDict.TryGetValue(key, out _);
	}

	// ========== GetHashCode Benchmarks ==========

	[Benchmark(Description = "GetHashCode: string key")]
	public int HashCode_String() => _precomputedStringKey.GetHashCode();

	[Benchmark(Description = "GetHashCode: DnsZoneLookupKey")]
	public int HashCode_ZoneStruct() => _precomputedZoneKey.GetHashCode();

	[Benchmark(Description = "GetHashCode: DnsRequestKey")]
	public int HashCode_RequestStruct() => _precomputedRequestKey.GetHashCode();
}