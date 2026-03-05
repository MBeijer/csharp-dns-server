using System.ComponentModel.DataAnnotations;

namespace Dns.Cli.Models;

/// <summary>
///     Request body for importing a single BIND zone file into a database-backed zone.
/// </summary>
public sealed class BindZoneImportRequest
{
	/// <summary>
	///     Absolute or relative path to the BIND zone file.
	/// </summary>
	[Required]
	[MaxLength(260)]
	public string FileName { get; set; } = string.Empty;

	/// <summary>
	///     Zone suffix to use as the database zone key (for example, "example.com").
	/// </summary>
	[Required]
	[MaxLength(120)]
	public string ZoneSuffix { get; set; } = string.Empty;

	/// <summary>
	///     Marks the imported database zone as enabled when true.
	/// </summary>
	public bool Enabled { get; set; } = true;

	/// <summary>
	///     Replaces existing database records for the same zone suffix when true.
	/// </summary>
	public bool ReplaceExistingRecords { get; set; } = true;
}