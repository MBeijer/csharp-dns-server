using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Dns.Db.Models.EntityFramework.Enums;

namespace Dns.Db.Models.EntityFramework;

[Table("zone_records")]
public class ZoneRecord
{
	[Key]
	[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
	[Column("id")]
	public int? Id { get; set; }

	[Column("host")] [MaxLength(120)] public string? Host { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	[Column("class")]
	public ResourceClass? Class { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	[Column("type")]
	public ResourceType? Type { get; set; }

	[Column("data")] public string? Data { get; set; }
	[Column("zone")] public int?    Zone { get; set; }

	[JsonIgnore] public Zone? ZoneObj { get; set; }
}