using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Dns.RDataTypes;

public class GenericRData : RData
{
	private readonly byte[] _bytes;

	private GenericRData(IReadOnlyList<byte> bytes, int offset, int size)
	{
		_bytes = new byte[size];

		for (var i = 0; i < size; i++)
			_bytes[i] = bytes[offset + i];
	}

	public override ushort Length => (ushort)_bytes.Length;

	public static GenericRData Parse(byte[] bytes, int offset, int size) => new(bytes, offset, size);

	public override void WriteToStream(Stream stream) => stream.Write(_bytes, 0, _bytes.Length);

	public override void Dump() => Console.WriteLine("Address:   {0}", JsonConvert.SerializeObject(this));
}