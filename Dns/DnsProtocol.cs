// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="DnsProtocol.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Text;

namespace Dns;

public class DnsProtocol
{
    /// <summary></summary>
    /// <param name="bytes"></param>
    /// <param name="dnsMessage"></param>
    /// <returns></returns>
    public static bool TryParse(byte[] bytes, out DnsMessage dnsMessage) => DnsMessage.TryParse(bytes, out dnsMessage);

    public static ushort ReadUshort(byte[] bytes, ref int offset)
    {
        var ret = BitConverter.ToUInt16(bytes, offset);
        offset += sizeof (ushort);
        return ret;
    }

    public static uint ReadUint(byte[] bytes, ref int offset)
    {
        var ret = BitConverter.ToUInt32(bytes, offset);
        offset += sizeof (uint);
        return ret;
    }

    private static readonly char[] trimChars = new[] {'.'};

    public static string ReadString(byte[] bytes, ref int currentOffset)
    {
        var resourceName = new StringBuilder();
        var compressionOffset = -1;
        while (true)
        {
            // get segment length or detect termination of segments
            int segmentLength = bytes[currentOffset];

            // compressed name
            if ((segmentLength & 0xC0) == 0xC0)
            {
                currentOffset++;
                if (compressionOffset == -1)
                {
                    // only record origin, and follow all pointers thereafter
                    compressionOffset = currentOffset;
                }

                // move pointer to compression segment
                currentOffset = bytes[currentOffset];
                segmentLength = bytes[currentOffset];
            }

            if (segmentLength == 0x00)
            {
                if (compressionOffset != -1)
                {
                    currentOffset = compressionOffset;
                }
                // move past end of name \0
                currentOffset++;
                break;
            }

            // move pass length and get segment text
            currentOffset++;
            resourceName.Append($"{Encoding.Default.GetString(bytes, currentOffset, segmentLength)}.");
            currentOffset += segmentLength;
        }
        return resourceName.ToString().TrimEnd(trimChars);
    }
}