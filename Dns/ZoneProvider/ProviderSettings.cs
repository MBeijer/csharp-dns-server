using System.Text.Json.Serialization;
using Dns.ZoneProvider.IPProbe;
using Dns.ZoneProvider.Traefik;

namespace Dns.ZoneProvider;

[JsonDerivedType(typeof(TraefikZoneProviderSettings), "traefik")]
[JsonDerivedType(typeof(IPProbeProviderSettings), "ipprobe")]
[JsonDerivedType(typeof(FileWatcherZoneProviderSettings), "filewatcher")]
public abstract class ProviderSettings;