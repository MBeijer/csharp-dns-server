// // //------------------------------------------------------------------------------------------------- 
// // // <copyright file="Zone.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Dns
{
    public class Zone : List<ZoneRecord>
    {
        public string Suffix { get; set; }

        public uint Serial { get; set; }

        public void Initialize(IEnumerable<ZoneRecord> nameRecords)
        {
            Clear();
            if (nameRecords != null)
                AddRange(nameRecords);
        }
    }
}