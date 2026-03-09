using System.Collections.Generic;

namespace Dns.Cli.Models.Dto;

/// <summary>
/// API representation of a DNS zone.
/// </summary>
public sealed class ZoneDto
{
	/// <summary>
	/// Zone identifier.
	/// </summary>
	public int? Id { get; set; }

	/// <summary>
	/// Authoritative suffix for the zone.
	/// </summary>
	public string? Suffix { get; set; }

	/// <summary>
	/// Zone serial.
	/// </summary>
	public uint Serial { get; set; }

	/// <summary>
	/// Indicates whether the zone is active.
	/// </summary>
	public bool Enabled { get; set; }

	/// <summary>
	/// Optional master zone identifier when this zone is configured as a slave.
	/// </summary>
	public int? MasterZoneId { get; set; }

	/// <summary>
	/// Master zone suffix for overview display.
	/// </summary>
	public string? MasterZoneSuffix { get; set; }

	/// <summary>
	/// Number of slave zones linked to this zone.
	/// </summary>
	public int SlaveZoneCount { get; set; }

	/// <summary>
	/// Zone records.
	/// </summary>
	public ICollection<ZoneRecordDto>? Records { get; set; }
}