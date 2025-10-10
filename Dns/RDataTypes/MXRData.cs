using System;
using System.IO;
using Dns.Extensions;
using Newtonsoft.Json;

namespace Dns.RDataTypes;

public class MXRData : RData
{
	private readonly ushort _preference;
	private readonly int    _size;
	private readonly string _name;


	private MXRData(byte[] bytes, int offset, int size)
	{
		_preference = BitConverter.ToUInt16(bytes, offset).SwapEndian();
		offset += 2;
		_name = DnsProtocol.ReadString(bytes, ref offset);
		_size = size;
	}

	public static MXRData Parse(byte[] bytes, int offset, int size) => new(bytes, offset, size);

	public override void WriteToStream(Stream stream)
	{
		var bytes = BitConverter.GetBytes(_preference.SwapEndian());

		stream.Write(bytes, 0, bytes.Length);
		_name.WriteToStream(stream);
	}

	public override ushort Length => (ushort)(_name.Length + 2 + 2);

	public override void Dump()
	{
		Console.WriteLine("Address:   {0}", JsonConvert.SerializeObject(this));
	}
}