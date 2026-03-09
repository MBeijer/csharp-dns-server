using System.IO;
using System.Linq;
using Dns;
using Dns.Models;
using Xunit;

namespace Dns.UnitTests;

public sealed class SmartAddressDispenserTests
{
	[Fact]
	public void GetAddresses_ReturnsRotatingAddresses()
	{
		var record = new ZoneRecord { Host = "www", Addresses = ["192.0.2.1", "192.0.2.2", "192.0.2.3"] };
		var target = new SmartAddressDispenser(record, 2);

		var first = target.GetAddresses().Select(ip => ip.ToString()).ToList();
		var second = target.GetAddresses().Select(ip => ip.ToString()).ToList();

		Assert.Equal(["192.0.2.1", "192.0.2.2"], first);
		Assert.Equal(["192.0.2.2", "192.0.2.3"], second);
	}

	[Fact]
	public void DumpHtmlAndGetObject_Work()
	{
		var record = new ZoneRecord { Host = "www", Addresses = ["192.0.2.1"] };
		var target = new SmartAddressDispenser(record);
		using var writer = new StringWriter();

		target.DumpHtml(writer);
		var value = target.GetObject();

		Assert.Contains("Sequence", writer.ToString());
		Assert.Same(record.Addresses, value);
	}
}