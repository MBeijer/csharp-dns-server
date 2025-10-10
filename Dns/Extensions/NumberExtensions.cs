using System;
using System.Text;

namespace Dns.Extensions;

public static class NumberExtensions
{
	public static string IP(this long ipLong)
	{
		var  b = new StringBuilder();

		var  tempLong = ipLong;
		var temp     = tempLong/(256*256*256);
		tempLong -= (temp*256*256*256);
		b.Append(Convert.ToString(temp)).Append('.');
		temp     =  tempLong/(256*256);
		tempLong -= (temp*256*256);
		b.Append(Convert.ToString(temp)).Append('.');
		temp     =  tempLong/256;
		tempLong -= (temp*256);
		b.Append(Convert.ToString(temp)).Append('.');
		temp     =  tempLong;
		tempLong -= temp;
		b.Append(Convert.ToString(temp));

		return b.ToString().ToLower();
	}
}