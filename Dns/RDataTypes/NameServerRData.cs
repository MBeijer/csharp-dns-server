using System;
using System.IO;

namespace Dns.RDataTypes;

public class NameServerRData : RData
{
	public string Name { get; init; }

	public static NameServerRData Parse(byte[] bytes, int offset, int size)
	{
		var nsRdata = new NameServerRData { Name = DnsProtocol.ReadString(bytes, ref offset) };
		return nsRdata;
	}

	public override ushort Length
	{
		// dots replaced by bytes
		// + 1 segment prefix
		// + 1 null terminator
		get { return (ushort)(Name.Length + 2); }
	}

	public override void WriteToStream(Stream stream)
	{
		Name.WriteToStream(stream);
	}


	public override void Dump()
	{
		Console.WriteLine("NameServer:   {0}", Name);
	}
}