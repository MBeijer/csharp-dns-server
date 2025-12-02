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
			var name = DnsProtocol.ReadString(bytes, ref currentOffset);

			var span = bytes.AsSpan(currentOffset);
			var type = (ResourceType)BinaryPrimitives.ReadUInt16BigEndian(span);
			currentOffset += 2;

			var lClass = (ResourceClass)BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
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