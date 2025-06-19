using System.Collections.Generic;

namespace Dns.Config;

public class ZoneOptions
{
	public string                             Name             { get; set; }
	public string                             Provider         { get; set; }
	public IReadOnlyDictionary<string,string> ProviderSettings { get; set; }
}