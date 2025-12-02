using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Dns.Db.Models.EntityFramework;

[Table("zones")]
public class Zone
{
	[Key]
	[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
	[Column("id")]
	public int? Id { get; set; }

	[Column("suffix")] [MaxLength(120)] public string? Suffix { get; set; }

	[Column("serial")]  public uint                     Serial  { get; set; }
	[Column("enabled")] public bool                     Enabled { get; set; }
	public                     ICollection<ZoneRecord>? Records { get; set; }
}