// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="QuestionList.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
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

                Question question = new Question();

                question.Name = DnsProtocol.ReadString(bytes, ref currentOffset);

                // Phase 5: Use BinaryPrimitives for zero-allocation reads
                var span = bytes.AsSpan(currentOffset);
                question.Type = (ResourceType)BinaryPrimitives.ReadUInt16BigEndian(span);
                currentOffset += 2;

                question.Class = (ResourceClass)BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2));
                currentOffset += 2;

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
