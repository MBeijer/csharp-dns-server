using System;
using System.IO;
using System.Text;

namespace Dns.RDataTypes;

public class TXTRData : RData
{
	private static readonly Encoding TxtEncoding = new UTF8Encoding(false, true);

	// ReSharper disable once IdentifierTypo
	public TXTRData()
	{
	}

	private TXTRData(byte[] bytes, int offset, int size)
	{
		ArgumentNullException.ThrowIfNull(bytes);
		if (size < 1) throw new InvalidDataException("TXT RDATA must contain at least one character-string.");
		if (offset < 0 || offset > bytes.Length - size)
			throw new InvalidDataException("TXT RDATA exceeds the DNS message boundary.");

		var end         = offset + size;
		var decoded     = new byte[size];
		var decodedSize = 0;
		while (offset < end)
		{
			var segmentLength = bytes[offset++];
			if (segmentLength > end - offset)
				throw new InvalidDataException("TXT character-string exceeds the RDATA boundary.");

			Buffer.BlockCopy(bytes, offset, decoded, decodedSize, segmentLength);
			offset      += segmentLength;
			decodedSize += segmentLength;
		}

		Name = TxtEncoding.GetString(decoded, 0, decodedSize);
	}

	public string Name { get; init; }

	public override ushort Length
	{
		get
		{
			var byteCount = TxtEncoding.GetByteCount(Name ?? string.Empty);
			var length    = byteCount == 0 ? 1 : byteCount + (byteCount + byte.MaxValue - 1) / byte.MaxValue;
			if (length > ushort.MaxValue) throw new InvalidDataException("TXT RDATA exceeds 65535 bytes.");

			return (ushort)length;
		}
	}

	public static TXTRData Parse(byte[] bytes, int offset, int size) => new(bytes, offset, size);

	public override void WriteToStream(Stream stream)
	{
		ArgumentNullException.ThrowIfNull(stream);
		_ = Length;

		var data = TxtEncoding.GetBytes(Name ?? string.Empty);
		if (data.Length == 0)
		{
			stream.WriteByte(0);
			return;
		}

		for (var offset = 0; offset < data.Length; offset += byte.MaxValue)
		{
			var segmentLength = Math.Min(byte.MaxValue, data.Length - offset);
			stream.WriteByte((byte)segmentLength);
			stream.Write(data, offset, segmentLength);
		}
	}

	public override void Dump() => Console.WriteLine("TXT:   {0}", Name);
}
