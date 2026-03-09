using System;
using System.IO;
using System.Linq;
using Dns.Extensions;

namespace Dns.RDataTypes;

public class NSRData : RData
{
	public string Name { get; init; }

	public override ushort Length =>
		(ushort)(
			(Name ?? string.Empty)
			.Split(['.'], StringSplitOptions.RemoveEmptyEntries)
			.Sum(segment => segment.Length + 1) + 1
		);

	public static NSRData Parse(byte[] bytes, int offset, int size) =>
		new() { Name = DnsProtocol.ReadString(bytes, ref offset) };

	public override void WriteToStream(Stream stream) => Name.WriteToStream(stream);

	public override void Dump()
	{
		Console.WriteLine("NameServer:   {0}", Name);
	}
}
