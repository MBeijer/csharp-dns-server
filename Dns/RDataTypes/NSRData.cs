using System;
using System.IO;

namespace Dns.RDataTypes;

public class NSRData : RData
{
	public string Name { get; init; }

	public override ushort Length =>
		// dots replaced by bytes
		// + 1 segment prefix
		// + 1 null terminator
		(ushort) (Name.Length + 2);

	public static NSRData Parse(byte[] bytes, int offset, int size) => new() { Name = DnsProtocol.ReadString(bytes, ref offset) };

	public override void WriteToStream(Stream stream) => Name.WriteToStream(stream);

	public override void Dump()
	{
		Console.WriteLine("CName:   {0}", Name);
	}
}