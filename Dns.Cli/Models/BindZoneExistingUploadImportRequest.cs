using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Dns.Cli.Models;

/// <summary>
///     Multipart form payload for importing a BIND zone file into an existing database zone.
/// </summary>
public sealed class BindZoneExistingUploadImportRequest
{
	/// <summary>
	///     Uploaded BIND zone file content.
	/// </summary>
	[Required]
	public IFormFile? File { get; set; }

	/// <summary>
	///     Replaces all records in the target zone when true; otherwise only new records are added.
	/// </summary>
	public bool ReplaceExistingRecords { get; set; } = true;
}