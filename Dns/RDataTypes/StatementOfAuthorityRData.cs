using System;
using System.IO;
using Dns.Extensions;

namespace Dns.RDataTypes;

public class StatementOfAuthorityRData : RData
{

	public string PrimaryNameServer               { get; set; }
	public string ResponsibleAuthoritativeMailbox { get; set; }
	public uint   Serial                          { get; set; }
	public uint   RefreshInterval                 { get; set; }
	public uint   RetryInterval                   { get; set; }
	public uint   ExpirationLimit                 { get; set; }
	public uint   MinimumTTL                      { get; set; }

	public static StatementOfAuthorityRData Parse(byte[] bytes, int offset, int size)
	{
		var soaRdata = new StatementOfAuthorityRData
		{
			PrimaryNameServer = DnsProtocol.ReadString(bytes, ref offset), ResponsibleAuthoritativeMailbox = DnsProtocol.ReadString(bytes, ref offset),
			Serial = DnsProtocol.ReadUint(bytes, ref offset).SwapEndian(),
			RefreshInterval = DnsProtocol.ReadUint(bytes, ref offset).SwapEndian(),
			RetryInterval = DnsProtocol.ReadUint(bytes, ref offset).SwapEndian(),
			ExpirationLimit = DnsProtocol.ReadUint(bytes, ref offset).SwapEndian(),
			MinimumTTL = DnsProtocol.ReadUint(bytes, ref offset).SwapEndian(),
		};
		return soaRdata;
	}

	public override ushort Length
	{
		// dots replaced by bytes
		// + 1 segment prefix
		// + 1 null terminator
		get { return (ushort) (PrimaryNameServer.Length + 2 + ResponsibleAuthoritativeMailbox.Length + 2 + 20); }
	}

	public override void WriteToStream(Stream stream)
	{
		PrimaryNameServer.WriteToStream(stream);
		ResponsibleAuthoritativeMailbox.WriteToStream(stream);
		Serial.SwapEndian().WriteToStream(stream);
		RefreshInterval.SwapEndian().WriteToStream(stream);
		RetryInterval.SwapEndian().WriteToStream(stream);
		ExpirationLimit.SwapEndian().WriteToStream(stream);
		MinimumTTL.SwapEndian().WriteToStream(stream);
	}

	public override void Dump()
	{
		Console.WriteLine("PrimaryNameServer:               {0}", PrimaryNameServer);
		Console.WriteLine("ResponsibleAuthoritativeMailbox: {0}", ResponsibleAuthoritativeMailbox);
		Console.WriteLine("Serial:                          {0}", Serial);
		Console.WriteLine("RefreshInterval:                 {0}", RefreshInterval);
		Console.WriteLine("RetryInterval:                   {0}", RetryInterval);
		Console.WriteLine("ExpirationLimit:                 {0}", ExpirationLimit);
		Console.WriteLine("MinimumTTL:                      {0}", MinimumTTL);
	}
}