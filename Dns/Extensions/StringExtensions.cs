using System.IO;
using System.Text;

namespace Dns.Extensions;

public static class StringExtensions
{
	private static readonly char[] separator = ['.'];

	public static byte[] GetResourceBytes(this string str, char delimiter = '.')
	{
		str ??= "";

		using var stream   = new MemoryStream(str.Length + 2);
		var       segments = str.Split(separator);
		foreach (var segment in segments)
		{
			stream.WriteByte((byte)segment.Length);
			foreach (var currentChar in segment)
			{
				stream.WriteByte((byte)currentChar);
			}
		}
		// null delimiter
		stream.WriteByte(0x0);
		return stream.GetBuffer();
	}

	public static byte[] GetBytes(this string str, Encoding encoding = null)
		=> (encoding??Encoding.ASCII).GetBytes(str);
}