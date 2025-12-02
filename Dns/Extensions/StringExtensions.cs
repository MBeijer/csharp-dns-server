using System.IO;
using System.Text;

namespace Dns.Extensions;

public static class StringExtensions
{
	public static byte[] GetResourceBytes(this string str, char delimiter = '.')
	{
		str ??= "";

		using var stream   = new MemoryStream(str.Length + 2);
		var       segments = str.Split(delimiter);
		foreach (var segment in segments)
		{
			stream.WriteByte((byte)segment.Length);
			foreach (var currentChar in segment) stream.WriteByte((byte)currentChar);
		}

		stream.WriteByte(0x0);
		return stream.GetBuffer();
	}

	public static byte[] GetBytes(this string str, Encoding encoding = null) =>
		(encoding ?? Encoding.ASCII).GetBytes(str);
}