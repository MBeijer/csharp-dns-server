using Newtonsoft.Json;

namespace Dns.ZoneProvider.Traefik.Models;

public class Tls
{
	[JsonProperty("certResolver")]
	public string CertResolver { get; set; }
}