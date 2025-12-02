// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="RData.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.IO;

namespace Dns.RDataTypes;

public abstract class RData
{
	public abstract ushort Length { get; }
	public abstract void   Dump();
	public abstract void   WriteToStream(Stream stream);
}