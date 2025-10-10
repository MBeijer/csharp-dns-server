using System.Text.Json.Serialization;

namespace Dns.ZoneProvider.IPProbe;

public class HostOptions
{
	/// <summary>Host name</summary>
	[JsonPropertyName("name")]
	public string Name { get; set; }

	/// <summary>Probe strategy</summary>
	[JsonPropertyName("probe")]
	public string Probe { get; set; }

	/// <summary>Host probe timeout</summary>
	[JsonPropertyName("timeout")]
	public ushort Timeout { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
	[JsonPropertyName("availabilityMode")]
	public AvailabilityMode AvailabilityMode { get; set; }

	[JsonPropertyName("ip")]
	public string[] Ip { get; set; }
}