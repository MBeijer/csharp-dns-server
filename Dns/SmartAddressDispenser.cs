// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="SmartAddressDispenser.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Net;
using Dns.Contracts;

namespace Dns;

/// <summary>Address Dispenser enables round-robin ordering for the specified zone record</summary>
public class SmartAddressDispenser(ZoneRecord record, ushort maxAddressesReturned = 4) : IAddressDispenser
{
    private ulong _sequence;

    string IAddressDispenser.HostName   => ZoneRecord.Host;
    public ZoneRecord        ZoneRecord { get; } = record;

    /// <summary>Returns round-robin rotated set of IP addresses</summary>
    /// <returns>Set of IP Addresses</returns>
    public IEnumerable<IPAddress> GetAddresses()
    {
        var addresses = ZoneRecord.Addresses.ToArray();

        if(addresses.Length == 0)
            yield break;

        // starting position in rollover list
        var start  = (int) (_sequence % (ulong) addresses.Length);
        var offset = start;

        uint count = 0;
        while (true)
        {
            yield return IPAddress.Parse(addresses[offset]);
            offset++;

            // rollover to start of list
            if (offset == addresses.Length) offset = 0;

            // if back at starting position then exit
            if (offset == start)
                break;

            // manage max number of dns entries returned
            count++;
            if (count == maxAddressesReturned)
                break;
        }
        // advance sequence
        _sequence++;
    }

    public void DumpHtml(TextWriter writer)
    {
        writer.WriteLine("Sequence:{0}", _sequence);
        foreach (var address in ZoneRecord.Addresses) writer.WriteLine(address);
    }

    public object GetObject() => ZoneRecord.Addresses;
}