using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Dns.Cli.Models;

/// <summary>
///     Multipart form payload for importing a BIND zone file upload into a database-backed zone.
/// </summary>
public sealed class BindZoneUploadImportRequest
{
	/// <summary>
	///     Uploaded BIND zone file content.
	/// </summary>
	[Required]
	public IFormFile? File { get; set; }

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