using System.Text.Json.Serialization;
using Dns.ZoneProvider.IPProbe;
using Dns.ZoneProvider.Traefik;

namespace Dns.ZoneProvider;

[JsonDerivedType(typeof(TraefikZoneProviderSettings), typeDiscriminator: "traefik")]
[JsonDerivedType(typeof(IPProbeProviderSettings), typeDiscriminator: "ipprobe")]
[JsonDerivedType(typeof(FileWatcherZoneProviderSettings), typeDiscriminator: "filewatcher")]
public abstract class ProviderSettings;