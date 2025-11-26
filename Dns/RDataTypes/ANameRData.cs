using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Dns.Db.Models.EntityFramework.Enums;

namespace Dns.RDataTypes;

public class ANameRData : RData
{
	private readonly IPAddress    _address;
	private readonly ResourceType _type = ResourceType.A;

	public ANameRData()
	{
	}

	private ANameRData(IReadOnlyList<byte> bytes, int offset, int size)
	{
		_type = size == 4 ? ResourceType.A : ResourceType.AAAA;
		var addressBytes = new byte[size]; // = BitConverter.(bytes, offset);

		var j = 0;
		for (var i = offset; i < offset + size; i++)
		{
			addressBytes[j] = bytes[i];
			j++;
		}

		Address = new(addressBytes);
	}

	public override ushort Length => (ushort)(_type == ResourceType.A ? 4 : 16);

	public IPAddress Address
	{
		get => _address;
		init
		{
			_type    = value.GetAddressBytes().Length == 4 ? ResourceType.A : ResourceType.AAAA;
			_address = value;
		}
	}

	public static ANameRData Parse(byte[] bytes, int offset, int size) => new(bytes, offset, size);

	public override void WriteToStream(Stream stream)
	{
		var bytes = Address.GetAddressBytes();
		stream.Write(bytes, 0, bytes.Length);
	}

	public override void Dump()
	{
		Console.WriteLine("Address:   {0}", Address);
	}
}