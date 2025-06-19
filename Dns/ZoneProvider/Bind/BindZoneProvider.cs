// // //------------------------------------------------------------------------------------------------- 
// // // <copyright file="BindZoneProvider.cs" >
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using Dns.Contracts;

namespace Dns.ZoneProvider.Bind;

public class BindZoneProvider(IDnsResolver resolver) : FileWatcherZoneProvider(resolver)
{
    public override Zone GenerateZone()
    {
        // RFC 1035 - https://tools.ietf.org/html/rfc1035
        // Forward scanning parser
        // while(not EOF)
        //    State is in record
        //    General Field list : Name Class Type [(Data 0..*)] EOR
        //    $ORIGIN [name]
        //    $TTL Timespan
        //    [Name|@] IN SOA Name
        //    [Name|@] IN NS Name
        //    [Name|@] IN MX Priority Name
        //    [Name|@] IN A IPv4
        //    [Name|@] IN AAAA IPv6
        //    [Name|@] IN CNAME name
        // endwhile

        throw new NotImplementedException();
    }

    public override void Dispose()
    {
        throw new NotImplementedException();
    }
}