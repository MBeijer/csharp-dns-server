// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="ResourceList.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Extensions;
using Dns.RDataTypes;

namespace Dns;

public class ResourceList : List<ResourceRecord>
{
	public int LoadFrom(byte[] bytes, int offset, ushort count)
	{
		var currentOffset = offset;

		for (var index = 0; index < count; index++)
		{
			// TODO: move this code into the Resource object

			var resourceRecord = new ResourceRecord
			{
				//// extract the domain, question type, question class and Ttl
				Name = DnsProtocol.ReadString(bytes, ref currentOffset),
				Type = (ResourceType)BitConverter.ToUInt16(bytes, currentOffset).SwapEndian(),
			};

			resourceRecord.Name = DnsProtocol.ReadString(bytes, ref currentOffset);

			// Phase 5: Use BinaryPrimitives for zero-allocation reads
			var span = bytes.AsSpan(currentOffset);
			resourceRecord.Type =  (ResourceType)BinaryPrimitives.ReadUInt16BigEndian(span);
			currentOffset       += sizeof(ushort);

			resourceRecord.Class =  (ResourceClass)BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
			currentOffset        += sizeof(ushort);

			resourceRecord.TTL =  BinaryPrimitives.ReadUInt32BigEndian(span[4..]);
			currentOffset      += sizeof(uint);

			resourceRecord.DataLength =  BinaryPrimitives.ReadUInt16BigEndian(span[8..]);
			currentOffset             += sizeof(ushort);

			if (resourceRecord.Class == ResourceClass.IN && resourceRecord.Type is ResourceType.A or ResourceType.AAAA)
				resourceRecord.RData = ANameRData.Parse(bytes, currentOffset, resourceRecord.DataLength);
			else
				resourceRecord.RData = resourceRecord.Type switch
				{
					ResourceType.CNAME => CNameRData.Parse(bytes, currentOffset, resourceRecord.DataLength),
					ResourceType.MX    => MXRData.Parse(bytes, currentOffset, resourceRecord.DataLength),
					ResourceType.SOA   => SOARData.Parse(bytes, currentOffset, resourceRecord.DataLength),
					ResourceType.NS    => NSRData.Parse(bytes, currentOffset, resourceRecord.DataLength),
					ResourceType.TXT  => TXTRData.Parse(bytes, currentOffset, resourceRecord.DataLength),
					_                  => GenericRData.Parse(bytes, currentOffset, resourceRecord.DataLength),
				};

			// move past resource data record
			currentOffset += resourceRecord.DataLength;

			Add(resourceRecord);
		}

		var bytesRead = currentOffset - offset;
		return bytesRead;
	}

	public void WriteToStream(Stream stream)
	{
		foreach (var resource in this) resource.WriteToStream(stream);
	}
}