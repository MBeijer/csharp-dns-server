using System;
using System.IO;
using Dns.Extensions;

namespace Dns.RDataTypes;

public class DomainNamePointRData : RData
{
	public string Name { get; set; }

	public override ushort Length => DnsProtocol.GetDomainNameWireLength(Name);

	public static DomainNamePointRData Parse(byte[] bytes, int offset, int size)
	{
		var domainName = new DomainNamePointRData { Name = DnsProtocol.ReadString(bytes, ref offset) };
		return domainName;
	}

	public override void WriteToStream(Stream stream)
	{
		Name.WriteToStream(stream);
	}

	public override void Dump()
	{
		Console.WriteLine("DName:   {0}", Name);
	}
}