using System.Collections.Generic;
using System.Linq;
using Dns.Db.Models.EntityFramework;
using Dns.Db.Models.EntityFramework.Enums;

namespace Dns.Cli.Models.Dto;

/// <summary>
/// Mapping helpers between API DTOs and EF entities.
/// </summary>
public static class DtoMappings
{
	/// <summary>
	/// Maps a user entity to a user DTO.
	/// </summary>
	/// <param name="user">Source user entity.</param>
	/// <returns>Mapped user DTO.</returns>
	public static UserDto ToDto(this User user) =>
		new()
		{
			Id = user.Id, Account = user.Account, Activated = user.Activated, AdminLevel = user.AdminLevel,
		};

	/// <summary>
	/// Maps a zone entity to a zone DTO.
	/// </summary>
	/// <param name="zone">Source zone entity.</param>
	/// <returns>Mapped zone DTO.</returns>
	public static ZoneDto ToDto(this Zone zone) =>
		new()
		{
			Id               = zone.Id,
			Suffix           = zone.Suffix,
			Serial           = zone.Serial,
			Enabled          = zone.Enabled,
			MasterZoneId     = zone.MasterZoneId,
			MasterZoneSuffix = zone.MasterZone?.Suffix,
			SlaveZoneCount   = zone.SlaveZones?.Count ?? 0,
			Source           = "Database",
			IsReadOnly       = zone.MasterZoneId != null,
			Records          = zone.Records?.Select(ToDto).ToList(),
		};

	/// <summary>
	/// Maps a runtime provider zone to a read-only DTO.
	/// </summary>
	/// <param name="zone">Runtime zone snapshot.</param>
	/// <param name="source">Provider display name.</param>
	/// <param name="isReplicated">Whether the provider is the automatic secondary provider.</param>
	/// <returns>Mapped read-only zone DTO.</returns>
	public static ZoneDto ToDto(this Dns.Models.Zone zone, string source, bool isReplicated) =>
		new()
		{
			Suffix       = zone.Suffix,
			Serial       = zone.Serial,
			Enabled      = true,
			Source       = source,
			IsReadOnly   = true,
			IsReplicated = isReplicated,
			Records      = zone.Records.SelectMany(record => ToDtos(record, zone.Serial)).ToList(),
		};

	/// <summary>
	/// Maps a zone record entity to a zone record DTO.
	/// </summary>
	/// <param name="record">Source zone record entity.</param>
	/// <returns>Mapped zone record DTO.</returns>
	public static ZoneRecordDto ToDto(this ZoneRecord record) =>
		new()
		{
			Id    = record.Id,
			Host  = record.Host,
			Class = record.Class,
			Type  = record.Type,
			Data  = record.Data,
			Zone  = record.Zone,
		};

	/// <summary>
	/// Maps a zone DTO to a zone entity.
	/// </summary>
	/// <param name="zoneDto">Source zone DTO.</param>
	/// <returns>Mapped zone entity.</returns>
	public static Zone ToEntity(this ZoneDto zoneDto) =>
		new()
		{
			Id           = zoneDto.Id,
			Suffix       = zoneDto.Suffix,
			Serial       = zoneDto.Serial,
			Enabled      = zoneDto.Enabled,
			MasterZoneId = zoneDto.MasterZoneId,
			Records = zoneDto.Records?.Select(record => record.ToEntity(zoneDto.Suffix)).ToList() ??
			          new List<ZoneRecord>(),
		};

	/// <summary>
	/// Maps a zone record DTO to a zone record entity.
	/// </summary>
	/// <param name="recordDto">Source zone record DTO.</param>
	/// <param name="zoneSuffix">Zone origin used to expand relative domain-name data.</param>
	/// <returns>Mapped zone record entity.</returns>
	public static ZoneRecord ToEntity(this ZoneRecordDto recordDto, string? zoneSuffix = null) =>
		new()
		{
			Id    = recordDto.Id,
			Host  = recordDto.Host,
			Class = recordDto.Class,
			Type  = recordDto.Type,
			Data  = NormalizeDomainNameData(recordDto.Type, recordDto.Data, zoneSuffix),
			Zone  = recordDto.Zone,
		};

	private static string? NormalizeDomainNameData(ResourceType? type, string? data, string? zoneSuffix)
	{
		if (string.IsNullOrWhiteSpace(data)) return data;

		return type switch
		{
			ResourceType.CNAME or ResourceType.NS or ResourceType.PTR => NormalizeDomainName(data, zoneSuffix),
			ResourceType.MX                                           => NormalizeMxData(data, zoneSuffix),
			ResourceType.SOA                                          => NormalizeSoaData(data, zoneSuffix),
			_                                                         => data,
		};
	}

	private static string NormalizeMxData(string data, string? zoneSuffix)
	{
		var fields = data.Split(' ', 2, System.StringSplitOptions.RemoveEmptyEntries);
		return fields.Length == 2 ? $"{fields[0]} {NormalizeDomainName(fields[1], zoneSuffix)}" : data;
	}

	private static string NormalizeSoaData(string data, string? zoneSuffix)
	{
		var fields = data.Replace("(", " ", System.StringComparison.Ordinal)
		                 .Replace(")", " ", System.StringComparison.Ordinal)
		                 .Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
		if (fields.Length < 2) return data;

		fields[0] = NormalizeDomainName(fields[0], zoneSuffix);
		fields[1] = NormalizeDomainName(fields[1], zoneSuffix);
		return string.Join(' ', fields);
	}

	private static string NormalizeDomainName(string data, string? zoneSuffix)
	{
		var target = data.Trim();
		if (target.EndsWith('.')) return target;

		var origin = zoneSuffix?.Trim().Trim('.') ?? string.Empty;
		if (string.IsNullOrWhiteSpace(origin)) return target;
		if (target is "@" or "\\@") return $"{origin}.";

		return $"{target}.{origin}.";
	}

	private static IEnumerable<ZoneRecordDto> ToDtos(Dns.Models.ZoneRecord record, uint serial)
	{
		if (record.Type == ResourceType.SOA)
		{
			var primaryNameServer = record.Addresses.ElementAtOrDefault(0) ?? string.Empty;
			var mailbox           = record.Addresses.ElementAtOrDefault(1) ?? string.Empty;
			var refresh           = record.Addresses.ElementAtOrDefault(2) ?? "3600";
			var retry             = record.Addresses.ElementAtOrDefault(3) ?? "600";
			var expire            = record.Addresses.ElementAtOrDefault(4) ?? "1209600";
			var minimum           = record.Addresses.ElementAtOrDefault(5) ?? "300";
			return
			[
				new()
				{
					Host  = record.Host,
					Class = record.Class,
					Type  = record.Type,
					Data  = $"{primaryNameServer} {mailbox} {serial} {refresh} {retry} {expire} {minimum}",
				},
			];
		}

		return record.Addresses.DefaultIfEmpty(string.Empty)
		             .Select(address => new ZoneRecordDto
			             {
				             Host = record.Host, Class = record.Class, Type = record.Type, Data = address,
			             }
		             );
	}
}