// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="RData.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Newtonsoft.Json;

namespace Dns
{
	public abstract class RData
    {
        public abstract void Dump();
        public abstract void WriteToStream(Stream stream);

        public abstract ushort Length { get; }

    }

    // ReSharper disable once InconsistentNaming
    public class SOARData : RData
    {
	    private readonly string _masterDomainName;
	    private readonly string _responsibleDomainName;
	    private uint _serialNumber;
	    private uint _refreshInterval;
	    private uint _retryInterval;
	    private uint _expireInterval;
	    private uint _ttl;

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

    // ReSharper disable once InconsistentNaming
    public class MXRData : RData
    {
	    private readonly ushort _preference;
	    private readonly int _size;
	    private readonly string _name;


	    private MXRData(byte[] bytes, int offset, int size)
	    {
		    _preference = BitConverter.ToUInt16(bytes, offset).SwapEndian();
		    offset += 2;
		    _name = DnsProtocol.ReadString(bytes, ref offset);
		    _size = size;
	    }

	    public static MXRData Parse(byte[] bytes, int offset, int size) => new MXRData(bytes, offset, size);

	    public override void WriteToStream(Stream stream)
	    {
		    byte[] bytes = BitConverter.GetBytes(_preference.SwapEndian());

		    stream.Write(bytes, 0, bytes.Length);
		    _name.WriteToStream(stream);
	    }

	    public override ushort Length => (ushort)(_name.Length + 2 + 2);

	    public override void Dump()
	    {
		    Console.WriteLine("Address:   {0}", JsonConvert.SerializeObject(this));
	    }
    }

    public class ANameRData : RData
    {
	    private readonly ResourceType _type;

	    public ANameRData() {}
	    private ANameRData(IReadOnlyList<byte> bytes, int offset, int size)
	    {
		    _type = size == 4 ? ResourceType.A : ResourceType.AAAA;
		    byte[] addressBytes = new byte[size];// = BitConverter.(bytes, offset);

		    int j = 0;
		    for (int i = offset; i < offset + size; i++)
		    {
			    addressBytes[j] = bytes[i];
			    j++;
		    }

		    Address = new IPAddress(addressBytes);
	    }

        public static ANameRData Parse(byte[] bytes, int offset, int size) => new(bytes, offset, size);

        public override void WriteToStream(Stream stream)
        {
            byte[] bytes = Address.GetAddressBytes();
            stream.Write(bytes, 0, bytes.Length);
        }

        public override ushort Length => (ushort)(_type == ResourceType.A?4:16);

        public IPAddress Address { get; set; }

        public override void Dump()
        {
            Console.WriteLine("Address:   {0}", Address);
        }
    }

    // ReSharper disable once InconsistentNaming
    public class TXTRData : RData
    {
	    public string Name { get; set; } = "";

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


    // ReSharper disable once InconsistentNaming
    public class NSRData : RData
    {
	    public string Name { get; set; }

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

    public class CNameRData : RData
    {
        public string Name { get; set; }

        public override ushort Length
        {
            // dots replaced by bytes
            // + 1 segment prefix
            // + 1 null terminator
            get { return (ushort) (Name.Length + 2); }
        }

        public static CNameRData Parse(byte[] bytes, int offset, int size)
        {
            CNameRData cname = new CNameRData();
            cname.Name = DnsProtocol.ReadString(bytes, ref offset);
            return cname;
        }

        public override void WriteToStream(Stream stream)
        {
            Name.WriteToStream(stream);
        }

        public override void Dump()
        {
            Console.WriteLine("CName:   {0}", Name);
        }
    }

    public class DomainNamePointRData : RData
    {
        public string Name { get; set; }

        public static DomainNamePointRData Parse(byte[] bytes, int offset, int size)
        {
            DomainNamePointRData domainName = new DomainNamePointRData();
            domainName.Name = DnsProtocol.ReadString(bytes, ref offset);
            return domainName;
        }

        public override void WriteToStream(Stream stream)
        {
            Name.WriteToStream(stream);
        }

        public override ushort Length
        {
            // dots replaced by bytes
            // + 1 segment prefix
            // + 1 null terminator
            get { return (ushort)(Name.Length + 2); }
        }

        public override void Dump()
        {
            Console.WriteLine("DName:   {0}", Name);
        }
    }

    public class NameServerRData : RData
    {
        public string Name { get; set; }

        public static NameServerRData Parse(byte[] bytes, int offset, int size)
        {
            NameServerRData nsRdata = new NameServerRData();
            nsRdata.Name = DnsProtocol.ReadString(bytes, ref offset);
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

    public class StatementOfAuthorityRData : RData
    {

        public string PrimaryNameServer { get; set; }
        public string ResponsibleAuthoritativeMailbox { get; set; }
        public uint Serial { get; set; }
        public uint RefreshInterval { get; set; }
        public uint RetryInterval { get; set; }
        public uint ExpirationLimit { get; set; }
        public uint MinimumTTL { get; set; }

        public static StatementOfAuthorityRData Parse(byte[] bytes, int offset, int size)
        {
            StatementOfAuthorityRData soaRdata = new StatementOfAuthorityRData();
            soaRdata.PrimaryNameServer = DnsProtocol.ReadString(bytes, ref offset);
            soaRdata.ResponsibleAuthoritativeMailbox = DnsProtocol.ReadString(bytes, ref offset);
            soaRdata.Serial = DnsProtocol.ReadUint(bytes, ref offset).SwapEndian();
            soaRdata.RefreshInterval = DnsProtocol.ReadUint(bytes, ref offset).SwapEndian();
            soaRdata.RetryInterval = DnsProtocol.ReadUint(bytes, ref offset).SwapEndian();
            soaRdata.ExpirationLimit = DnsProtocol.ReadUint(bytes, ref offset).SwapEndian();
            soaRdata.MinimumTTL = DnsProtocol.ReadUint(bytes, ref offset).SwapEndian();
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

}
