using System;
using System.Net;
using System.Text;

namespace Dns.Extensions;

public static class NumberExtensions
{
	public static string IP(this long ipLong)
		=> new IPAddress(ipLong).ToString().ToLower();
}