﻿// // //------------------------------------------------------------------------------------------------- 
// // // <copyright file="ZoneProvider.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

namespace Dns.ZoneProvider.AP
{
    using System;
    using System.IO;
    using System.Net;
    using System.Linq;
    using Dns.Utility;
    using Dns.ZoneProvider;


    /// <summary>Source of Zone records</summary>
    public class APZoneProvider : FileWatcherZoneProvider
    {
        private string _zoneSuffix;

        private DateTime _lastGenerated = DateTime.MinValue;
        private uint _serial;

        public APZoneProvider(string machineInfoFile, string zoneSuffix) : base(machineInfoFile)
        {
            this.Initialize(machineInfoFile, zoneSuffix);
        }

        /// <summary>Initialize ZoneProvider</summary>
        /// <param name="machineInfoFile"></param>
        /// <param name="zoneSuffix"></param>
        public void Initialize(string machineInfoFile, string zoneSuffix)
        {
            _zoneSuffix = zoneSuffix;
        }

        public override Zone GenerateZone()
        {
            if (!File.Exists(this.Filename))
            {
                return null;
            }

            CsvParser parser = CsvParser.Create(this.Filename);
            var machines = parser.Rows.Select(row => new {MachineFunction = row["MachineFunction"], StaticIP = row["StaticIP"], MachineName = row["MachineName"]}).ToArray();

            var zoneRecords = machines
                            .GroupBy(machine => machine.MachineFunction + _zoneSuffix, machine => IPAddress.Parse(machine.StaticIP))
                            .Select(group => new ZoneRecord {Host = group.Key, Count = group.Count(), Addresses = group.Select(address => address).ToArray()})
                            .ToArray();

            Zone zone = new Zone();
            zone.Suffix = _zoneSuffix;
            zone.Serial = _serial;
            zone.Initialize(zoneRecords);

            // increment serial number
            _serial++;

            _lastGenerated = DateTime.Now;

            return zone;
        }

        public override void DumpHtml(TextWriter writer) {
            writer.WriteLine(String.Format("Filename:{0}",this.Filename));
            writer.WriteLine(String.Format("Last Updated:{0}",this._lastGenerated));
        }
    }
}