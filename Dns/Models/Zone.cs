// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="Zone.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Dns.Models;

public class Zone
{
	public string Suffix { get; set; }

	public uint Serial { get; set; }

	public List<ZoneRecord> Records { get; } = [];

	public void Initialize(IEnumerable<ZoneRecord> nameRecords)
	{
		Records.Clear();
		if (nameRecords != null)
			Records.AddRange(nameRecords);
	}
}