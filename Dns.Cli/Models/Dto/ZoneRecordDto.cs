using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Dns.Db.Models.EntityFramework.Enums;

namespace Dns.Cli.Models.Dto;

/// <summary>
/// API representation of a DNS zone record.
/// </summary>
public sealed class ZoneRecordDto : IValidatableObject
{
	/// <summary>
	/// Zone record identifier.
	/// </summary>
	public int? Id { get; set; }

	/// <summary>
	/// Host label of the record.
	/// </summary>
	public string? Host { get; set; }

	/// <summary>
	/// DNS resource class.
	/// </summary>
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ResourceClass? Class { get; set; }

	/// <summary>
	/// DNS resource type.
	/// </summary>
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ResourceType? Type { get; set; }

	/// <summary>
	/// Record payload, e.g. IP address or canonical name.
	/// </summary>
	public string? Data { get; set; }

	/// <summary>
	/// Parent zone identifier.
	/// </summary>
	public int? Zone { get; set; }

	/// <inheritdoc />
	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if (Type is not (ResourceType.A or ResourceType.AAAA)) yield break;

		var data         = Data?.Trim() ?? string.Empty;
		var expectedType = Type.Value;
		var valid        = expectedType == ResourceType.A ? IsIpv4Address(data) : IsIpv6Address(data);
		if (valid) yield break;

		var host           = string.IsNullOrWhiteSpace(Host) ? "@" : Host.Trim();
		var expectedFamily = expectedType == ResourceType.A ? "IPv4" : "IPv6";
		var suggestedType  = string.Empty;
		if (expectedType == ResourceType.A && IsIpv6Address(data))
			suggestedType = " Use an AAAA record for IPv6 addresses.";
		else if (expectedType == ResourceType.AAAA && IsIpv4Address(data))
			suggestedType = " Use an A record for IPv4 addresses.";

		yield return new(
			$"{expectedType} record '{host}' must contain a valid {expectedFamily} address.{suggestedType}",
			[nameof(Data)]
		);
	}

	private static bool IsIpv4Address(string value)
	{
		var segments = value.Split('.');
		return segments.Length == 4 &&
		       segments.All(segment => segment.Length is > 0 and <= 3 &&
		                               segment.All(char.IsAsciiDigit) &&
		                               byte.TryParse(segment, out _)
		       );
	}

	private static bool IsIpv6Address(string value) =>
		!value.Contains('%') &&
		IPAddress.TryParse(value, out var address) &&
		address.AddressFamily == AddressFamily.InterNetworkV6;
}