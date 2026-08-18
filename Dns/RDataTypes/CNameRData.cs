using System;
using System.IO;
using Dns.Extensions;

namespace Dns.RDataTypes;

public class CNameRData : RData
{
	public string Name { get; set; }

	public override ushort Length => DnsProtocol.GetDomainNameWireLength(Name);

	public static CNameRData Parse(byte[] bytes, int offset, int size)
	{
		var cname = new CNameRData { Name = DnsProtocol.ReadString(bytes, ref offset) };
		return cname;
	}

	public override void WriteToStream(Stream stream)
	{
		Name.WriteToStream(stream);
	}

	public override void Dump()
	{
		Console.WriteLine("CName:   {0}", Name);
	}
}