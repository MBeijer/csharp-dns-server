// // //------------------------------------------------------------------------------------------------- 
// // // <copyright file="ZoneRecord.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.Net;

namespace Dns
{
    public class ZoneRecord
    {
        public string Host;
        public ResourceClass Class = ResourceClass.IN;
        public ResourceType Type = ResourceType.A;
        public IPAddress[] Addresses;
        public int Count;
    }
}