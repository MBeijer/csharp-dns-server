// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="IHtmlDump.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.IO;

namespace Dns.Contracts;

public interface IHtmlDump
{
    void   DumpHtml(TextWriter writer);
    object GetObject();
}