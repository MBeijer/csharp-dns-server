// // //------------------------------------------------------------------------------------------------- 
// // // <copyright file="IDnsResolver.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.Net;

namespace Dns.Contracts
{
    /// <summary>Provides domain name resolver capabilities</summary>
    internal interface IDnsResolver : IHtmlDump
    {
        string GetZoneName();

        uint GetZoneSerial();

        bool TryGetHostEntry(string hostname, ResourceClass resClass, ResourceType resType, out IPHostEntry entry);
    }
}