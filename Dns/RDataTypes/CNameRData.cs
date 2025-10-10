using System;
using System.IO;
using Dns.Extensions;

namespace Dns.RDataTypes;

public class CNameRData : RData
{
	public string Name { get; set; }

	public override ushort Length
	{
		// dots replaced by bytes
		// + 1 segment prefix
		// + 1 null terminator
		get { return (ushort) (Name.Length + 2); }
	}

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