// // //------------------------------------------------------------------------------------------------- 
// // // <copyright file="ZoneProvider.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.IO;
using System.Linq;
using System.Net;
using Dns.Utility;

namespace Dns.ZoneProvider.AP
{
    /// <summary>Source of Zone records</summary>
    public class APZoneProvider : FileWatcherZoneProvider
    {

        public override Zone GenerateZone()
        {
            if (!File.Exists(Filename))
            {
                return null;
            }

            var parser = CsvParser.Create(Filename);
            var machines = parser.Rows.Select(row => new {MachineFunction = row["MachineFunction"], StaticIP = row["StaticIP"], MachineName = row["MachineName"]}).ToArray();

            var zoneRecords = machines
                            .GroupBy(machine => machine.MachineFunction + Zone, machine => IPAddress.Parse(machine.StaticIP))
                            .Select(group => new ZoneRecord {Host = group.Key, Count = group.Count(), Addresses = group.Select(address => address).ToArray()})
                            .ToArray();

            Zone zone = new() { Suffix = Zone, Serial = _serial };
            zone.Initialize(zoneRecords);

            // increment serial number
            _serial++;
            return zone;
        }
    }
}