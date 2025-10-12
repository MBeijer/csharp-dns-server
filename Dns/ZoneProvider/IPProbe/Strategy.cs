using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;

namespace Dns.ZoneProvider.IPProbe;

public class Strategy
{
    public delegate bool Probe(ILogger logger, IPAddress addr, ushort timeout);

    private static readonly Dictionary<string, Probe> ProbeFunctions = new();

    static Strategy()
    {
        ProbeFunctions["ping"] = Ping;
        ProbeFunctions["noop"] = NoOp;
    }

    public static Probe Get(string name) => ProbeFunctions.GetValueOrDefault(name, NoOp);

    private static bool Ping(ILogger logger, IPAddress address, ushort timeout)
    {
        logger.LogInformation("Ping: pinging {Address}", address);
        Ping sender    = new();
        var  pingReply = sender.Send(address, timeout);
        return pingReply?.Status == IPStatus.Success;
    }

    private static bool NoOp(ILogger logger, IPAddress address, ushort _) => true;
}