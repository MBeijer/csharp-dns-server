namespace Dns.Cli.Models;

/// <summary>
///     Options controlling the bulk import of active BIND zones into database-backed zones.
/// </summary>
public sealed class ActiveBindImportRequest
{
	/// <summary>
	///     Replaces existing database records for each imported zone suffix when true.
	/// </summary>
	public bool ReplaceExistingRecords { get; set; } = true;
	/// <summary>
	///     Sets imported database zones to enabled when true.
	/// </summary>
	public bool EnableImportedZones { get; set; } = true;
}