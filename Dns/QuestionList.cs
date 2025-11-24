// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="QuestionList.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Extensions;
using Dns.Models.Dns.Packets;
using Dns.Serializers;

namespace Dns;

public class QuestionList : List<Question>
{
    public int LoadFrom(byte[] bytes, int offset, ushort count)
    {
        var currentOffset = offset;

        for (var index = 0; index < count; index++)
        {
            // TODO: move this code into the Question object

            var name = DnsProtocol.ReadString(bytes, ref currentOffset);
            var type = (ResourceType)(BitConverter.ToUInt16(bytes, currentOffset).SwapEndian());
            currentOffset += 2;
            var lClass =  (ResourceClass) (BitConverter.ToUInt16(bytes, currentOffset).SwapEndian());
            currentOffset  += 2;

            Add(new(name, type, lClass));
        }

        var bytesRead = currentOffset - offset;
        return bytesRead;
    }

    public long WriteToStream(Stream stream)
    {
        var start = stream.Length;
        foreach (var question in this) stream.Write(question.Serialize().Buffer);
        var end = stream.Length;
        return end - start;
    }
}