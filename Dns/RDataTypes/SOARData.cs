using System;
using System.IO;
using Newtonsoft.Json;

namespace Dns.RDataTypes;

public class SOARData : RData
{
	private readonly string _masterDomainName;
	private readonly string _responsibleDomainName;
	private readonly uint   _serialNumber;
	private readonly uint   _refreshInterval;
	private readonly uint   _retryInterval;
	private readonly uint   _expireInterval;
	private readonly uint   _ttl;

	private SOARData(byte[] bytes, int offset, int size)
	{
		_masterDomainName = DnsProtocol.ReadString(bytes, ref offset);
		_responsibleDomainName = DnsProtocol.ReadString(bytes, ref offset);

		_serialNumber = BitConverter.ToUInt32(bytes, offset).SwapEndian();
		offset += 4;
		_refreshInterval = BitConverter.ToUInt32(bytes, offset).SwapEndian();
		offset += 4;
		_retryInterval = BitConverter.ToUInt32(bytes, offset).SwapEndian();
		offset += 4;
		_expireInterval = BitConverter.ToUInt32(bytes, offset).SwapEndian();
		offset += 4;
		_ttl = BitConverter.ToUInt32(bytes, offset).SwapEndian();

	}

	public static SOARData Parse(byte[] bytes, int offset, int size) => new(bytes, offset, size);

	public override void WriteToStream(Stream stream)
	{
		_masterDomainName.WriteToStream(stream);
		_responsibleDomainName.WriteToStream(stream);
		_serialNumber.SwapEndian().WriteToStream(stream);
		_refreshInterval.SwapEndian().WriteToStream(stream);
		_retryInterval.SwapEndian().WriteToStream(stream);
		_expireInterval.SwapEndian().WriteToStream(stream);
		_ttl.SwapEndian().WriteToStream(stream);
	}

	public override ushort Length => (ushort)(_masterDomainName.Length + 2 + _responsibleDomainName.Length + 2 + (4*5));

	public override void Dump()
	{
		Console.WriteLine("Address:   {0}", JsonConvert.SerializeObject(this));
	}
}