using System.Collections.Generic;

namespace Dns.Services;

public sealed class BindZoneImportBatchResult
{
	public int ImportedCount { get; set; }
	public int FailedCount { get; set; }
	public int DisabledCount { get; set; }
	public List<BindZoneImportBatchItem> Items { get; set; } = [];
}

public sealed class BindZoneImportBatchItem
{
	public string ZoneSuffix { get; set; } = string.Empty;
	public string FileName { get; set; } = string.Empty;
	public bool Imported { get; set; }
	public bool Disabled { get; set; }
	public string Error { get; set; } = string.Empty;
	public int? ZoneId { get; set; }
	public uint? Serial { get; set; }
	public int? RecordCount { get; set; }
}