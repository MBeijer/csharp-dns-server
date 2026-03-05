using System.Text.Json.Serialization;

namespace Dns.ZoneProvider.Traefik.Models;

public class Tls
{
	[JsonPropertyName("certResolver")] public string CertResolver { get; set; }
}
