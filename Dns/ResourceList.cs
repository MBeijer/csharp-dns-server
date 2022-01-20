// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="ResourceList.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;

namespace Dns
{
	public class ResourceList : List<ResourceRecord>
    {
        public int LoadFrom(byte[] bytes, int offset, ushort count)
        {
            int currentOffset = offset;

            for (int index = 0; index < count; index++)
            {
                // TODO: move this code into the Resource object

                ResourceRecord resourceRecord = new ResourceRecord();
                //// extract the domain, question type, question class and Ttl

                resourceRecord.Name = DnsProtocol.ReadString(bytes, ref currentOffset);

                resourceRecord.Type = (ResourceType) (BitConverter.ToUInt16(bytes, currentOffset).SwapEndian());
                currentOffset += sizeof (ushort);

                resourceRecord.Class = (ResourceClass) (BitConverter.ToUInt16(bytes, currentOffset).SwapEndian());
                currentOffset += sizeof (ushort);

                resourceRecord.TTL = BitConverter.ToUInt32(bytes, currentOffset).SwapEndian();
                currentOffset += sizeof (uint);

                resourceRecord.DataLength = BitConverter.ToUInt16(bytes, currentOffset).SwapEndian();
                currentOffset += sizeof (ushort);

                if (resourceRecord.Class == ResourceClass.IN && resourceRecord.Type == ResourceType.A)
                {
                    resourceRecord.RData = ANameRData.Parse(bytes, currentOffset, resourceRecord.DataLength);
                }
                else if (resourceRecord.Type == ResourceType.CNAME)
                {
                    resourceRecord.RData = CNameRData.Parse(bytes, currentOffset, resourceRecord.DataLength);
                }
                else if (resourceRecord.Type == ResourceType.MX)
                {
	                resourceRecord.RData = MXRData.Parse(bytes, currentOffset, resourceRecord.DataLength);
                }
                else if (resourceRecord.Type == ResourceType.SOA)
                {
	                resourceRecord.RData = SOARData.Parse(bytes, currentOffset, resourceRecord.DataLength);
                }
                else
                {
	                Console.WriteLine(resourceRecord.Type);
                }

                // move past resource data record
                currentOffset = currentOffset + resourceRecord.DataLength;

                Add(resourceRecord);
            }

            int bytesRead = currentOffset - offset;
            return bytesRead;
        }

        public void WriteToStream(Stream stream)
        {
            foreach (var resource in this)
            {
                resource.WriteToStream(stream);
            }
        }
    }
}
