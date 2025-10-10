using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dns.Extensions;

public static class StreamExtensions
{
	public static TextWriter CreateWriter(this Stream stream, Encoding encoding = null) =>
		new StreamWriter(stream, encoding ?? Encoding.UTF8);

	public static void WriteToStream(this string str, Stream stream, char segmentSplit = '.')
	{
		if (!string.IsNullOrWhiteSpace(str))
		{
			var segments = str.Split(new[] { segmentSplit });
			foreach (var segment in segments)
			{
				stream.WriteByte((byte)segment.Length);
				foreach (var currentChar in segment)
				{
					stream.WriteByte((byte)currentChar);
				}
			}
		}

		stream.WriteByte(0x0);
	}

	public static void WriteToStream2(this string str, Stream stream)
	{
		if (string.IsNullOrWhiteSpace(str)) return;

		stream.WriteByte((byte)str.Length);
		foreach (var currentChar in str)
		{
			stream.WriteByte((byte)currentChar);
		}
	}

	public static void WriteToStream(this IEnumerable<byte> chars, Stream stream)
	{
		foreach (var currentChar in chars)
		{
			stream.WriteByte(currentChar);
		}
	}

	public static void WriteToStream(this ushort value, Stream stream)
	{
		stream.WriteByte((byte)(value & 0xFF));
		stream.WriteByte((byte)((value >> 8) & 0xFF));
	}

	public static void WriteToStream(this uint value, Stream stream)
	{
		stream.WriteByte((byte)(value & 0xFF));
		stream.WriteByte((byte)((value >> 8) & 0xFF));
		stream.WriteByte((byte)((value >> 16) & 0xFF));
		stream.WriteByte((byte)((value >> 24) & 0xFF));
	}
}