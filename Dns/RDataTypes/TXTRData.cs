using System;
using System.IO;
using Dns.Extensions;

namespace Dns.RDataTypes;

public class TXTRData : RData
{
	public string Name { get; init; }

	public override ushort Length =>
		// dots replaced by bytes
		// + 1 segment prefix
		// + 1 null terminator
		(ushort) (Name.Length + 1);

	// ReSharper disable once IdentifierTypo
	private TXTRData(byte[] bytes, int offset, int size) => Name = DnsProtocol.ReadString(bytes, ref offset)[..(size-1)].Trim();


	public static TXTRData Parse(byte[] bytes, int offset, int size) => new(bytes, offset, size);

	public override void WriteToStream(Stream stream) => Name.WriteToStream2(stream);

	public override void Dump() => Console.WriteLine("CName:   {0}", Name);
}