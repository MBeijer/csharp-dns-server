// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="ZoneRecord.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Dns;

public class ZoneRecord
{
    public string        Host      { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResourceClass Class     { get; set; } = ResourceClass.IN;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResourceType  Type      { get; set; } = ResourceType.A;
    public List<string>  Addresses { get; set; }
    public int           Count     { get; set; }
}