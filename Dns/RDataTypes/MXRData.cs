using System;
using System.IO;
using Dns.Extensions;
using Newtonsoft.Json;

namespace Dns.RDataTypes;

public class MXRData : RData
{
	internal string Name       { get; init; }
	internal ushort Preference { get; init; }

	public MXRData() { }

	private MXRData(byte[] bytes, int offset)
	{
		Preference =  BitConverter.ToUInt16(bytes, offset).SwapEndian();
		offset      += 2;
		Name       =  DnsProtocol.ReadString(bytes, ref offset);
	}

	public override ushort Length => (ushort)(Name.Length + 2 + 2);

	public static MXRData Parse(byte[] bytes, int offset, int size) => new(bytes, offset);

	public override void WriteToStream(Stream stream)
	{
		var bytes = BitConverter.GetBytes(Preference.SwapEndian());

		stream.Write(bytes, 0, bytes.Length);
		Name.WriteToStream(stream);
	}

	public override void Dump()
	{
		Console.WriteLine("Address:   {0}", JsonConvert.SerializeObject(this));
	}
}