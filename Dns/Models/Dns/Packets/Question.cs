// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="Question.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.IO;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Extensions;
using Dns.Serializers.Attributes;

namespace Dns.Models.Dns.Packets;

public class Question(string name, ResourceType type, ResourceClass pClass) : GenericPacket
{
	[DynamicLengthString] public string Name { get; set; } = name;

	public ResourceType  Type  { get; set; } = type;
	public ResourceClass Class { get; set; } = pClass;

	public void WriteToStream(Stream stream)
	{
		var name = Name.GetResourceBytes();
		stream.Write(name, 0, name.Length);

		// Type
		stream.Write(BitConverter.GetBytes(((ushort)Type).SwapEndian()), 0, 2);

		// Class
		stream.Write(BitConverter.GetBytes(((ushort)Class).SwapEndian()), 0, 2);
	}
}