using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Dns.Config;

public class ZoneTransferOptions
{
	[JsonConverter(typeof(FlexibleBooleanJsonConverter))]
	[JsonPropertyName("enabled")] public bool Enabled { get; set; }

	[JsonPropertyName("allowTransfersFrom")]
	[JsonConverter(typeof(FlexibleStringListJsonConverter))]
	public List<string> AllowTransfersFrom { get; set; } = [];

	[JsonPropertyName("notifySecondaries")]
	[JsonConverter(typeof(FlexibleStringListJsonConverter))]
	public List<string> NotifySecondaries { get; set; } = [];

	[JsonPropertyName("notifyPollIntervalSeconds")]
	public int NotifyPollIntervalSeconds { get; set; } = 5;

	[JsonPropertyName("injectedNsAddress")]
	public string InjectedNsAddress { get; set; }
}