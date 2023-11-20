// // //------------------------------------------------------------------------------------------------- 
// // // <copyright file="IAddressDispenser.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;

namespace Dns.Contracts
{
    public interface IAddressDispenser : IHtmlDump
    {
        string HostName { get; }

        IEnumerable<IPAddress> GetAddresses();
    }
}