using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;

namespace Dns.ZoneProvider.IPProbe;

public class Strategy
{

    public delegate bool Probe(IPAddress addr, ushort timeout);

    // Probe Strategy Dictionary, maps configuration to implemented functions
    private static Dictionary<string, Probe> probeFunctions = new();

    static Strategy()
    {
        // New probe strategies and enhancements can be added here
        probeFunctions["ping"] = Ping;
        probeFunctions["noop"] = NoOp;
    }

    public static Probe Get(string name) => probeFunctions.GetValueOrDefault(name, NoOp);

    private static bool Ping(IPAddress address, ushort timeout)
    {
        Console.WriteLine("Ping: pinging {0}", address);
        Ping sender = new();
        PingOptions options = new(64, true);
        var pingReply = sender.Send(address, timeout);
        return pingReply?.Status == IPStatus.Success;
    }

    private static bool NoOp(IPAddress address, ushort _) => true;
}