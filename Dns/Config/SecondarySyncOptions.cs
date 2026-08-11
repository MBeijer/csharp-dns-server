// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="SecondarySyncOptions.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Dns.Config;

public sealed class SecondarySyncOptions
{
	[JsonConverter(typeof(FlexibleBooleanJsonConverter))]
	[JsonPropertyName("enabled")]
	public bool Enabled { get; set; }

	[JsonPropertyName("master")] public string Master { get; set; }

	[JsonPropertyName("reconnectDelaySeconds")]
	public int ReconnectDelaySeconds { get; set; } = 5;

	[JsonPropertyName("cacheFile")] public string CacheFile { get; set; }
}