namespace Dns.ZoneProvider.IPProbe;

public class HostOptions
{
	/// <summary>Host name</summary>
	public string Name { get; set; }

	/// <summary>Probe strategy</summary>
	public string Probe { get; set; }

	/// <summary>Host probe timeout</summary>
	public ushort Timeout { get; set; }

	public AvailabilityMode AvailabilityMode { get; set; }

	public string[] Ip { get; set; }
}