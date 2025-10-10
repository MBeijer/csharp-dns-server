using System;
using System.IO;
using Dns.Extensions;

namespace Dns.RDataTypes;

public class DomainNamePointRData : RData
{
	public string Name { get; set; }

	public static DomainNamePointRData Parse(byte[] bytes, int offset, int size)
	{
		var domainName = new DomainNamePointRData { Name = DnsProtocol.ReadString(bytes, ref offset) };
		return domainName;
	}

	public override void WriteToStream(Stream stream)
	{
		Name.WriteToStream(stream);
	}

	public override ushort Length
	{
		// dots replaced by bytes
		// + 1 segment prefix
		// + 1 null terminator
		get { return (ushort)(Name.Length + 2); }
	}

	public override void Dump()
	{
		Console.WriteLine("DName:   {0}", Name);
	}
}