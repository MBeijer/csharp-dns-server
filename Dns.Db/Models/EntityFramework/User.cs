using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Dns.Db.Models.EntityFramework;

[Table("users")]
public class User
{
	[Key, DatabaseGenerated(DatabaseGeneratedOption.Identity), Column("id")] public int Id { get; set; }

	[Column("account")][MaxLength(100)] public string? Account { get; set; }

	[JsonIgnore, Column("password"), MaxLength(450)]
	public string? Password { get; set; }

	[Column("activated")] public bool Activated { get; set; }

	[Column("adminlevel")] public byte AdminLevel { get; set; }
}