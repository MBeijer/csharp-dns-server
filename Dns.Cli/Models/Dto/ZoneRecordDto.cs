using System.Text.Json.Serialization;
using Dns.Db.Models.EntityFramework.Enums;

namespace Dns.Cli.Models.Dto;

/// <summary>
/// API representation of a DNS zone record.
/// </summary>
public sealed class ZoneRecordDto
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
}

