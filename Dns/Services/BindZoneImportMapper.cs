using System.Linq;
using Dns.Db.Models.EntityFramework;
using Dns.Db.Models.EntityFramework.Enums;

namespace Dns.Services;

public static class BindZoneImportMapper
{
	public static Zone ToDbZone(Models.Zone parsedZone, string zoneSuffix, bool enabled)
	{
		return new()
		{
			Suffix = zoneSuffix.Trim(),
			Serial = parsedZone.Serial,
			Enabled = enabled,
			Records = parsedZone.Records
							  .SelectMany(record => record.Addresses.Select(address => new ZoneRecord
							  {
								  Host = record.Host,
								  Class = (ResourceClass?)record.Class,
								  Type = (ResourceType?)record.Type,
								  Data = address,
							  }
							  ))
							  .ToList(),
		};
	}
}