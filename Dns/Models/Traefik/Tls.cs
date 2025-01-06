using Newtonsoft.Json;

namespace Dns.Models.Traefik;

public class Tls
{
	[JsonProperty("certResolver")]
	public string CertResolver { get; set; }
}