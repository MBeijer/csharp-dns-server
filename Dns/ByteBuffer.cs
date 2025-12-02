using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// ReSharper disable NonReadonlyMemberInGetHashCode

namespace Dns;

public class ByteBuffer
{
	private static readonly char[]     TrimChars = ['.'];
	private readonly        List<byte> _mBuffer  = [];
	private                 int        _mReadPosition;

	public ByteBuffer(string buffer = "") => Write(Encoding.Default.GetBytes(buffer));
	private ByteBuffer(IEnumerable<byte> str) => _mBuffer = str.ToList();

	public byte this[int index]
	{
		get => index >= 0 && index <= _mBuffer.Count - 1 ? _mBuffer[index] : (byte)0;
		set
		{
			if (index >= 0 && index <= _mBuffer.Count - 1)
				_mBuffer[index] = value;
		}
	}

	/// <summary>
	///     Return Byte-Array of Buffer
	/// </summary>
	public ReadOnlySpan<byte> Buffer => _mBuffer.ToArray();

	/// <summary>
	///     Return Bytes Left (Unread Bytes)
	/// </summary>
	public int BytesLeft => Math.Max(0, Length - ReadPosition);

	/// <summary>
	///     Return Capacity of Buffer
	/// </summary>
	public int Capacity
	{
		get => _mBuffer.Capacity;
		set => _mBuffer.Capacity = value;
	}

	/// <summary>
	///     Return Total Length of Buffer
	/// </summary>
	public int Length => _mBuffer.Count;

	/// <summary>
	///     Return/Set Currently Read Bytes
	/// </summary>
	public int ReadPosition
	{
		get => _mReadPosition;
		set => _mReadPosition = Math.Max(Math.Min(value, _mBuffer.Count), 0);
	}

	/// <summary>
	///     Return Text-Encoding of Buffer
	/// </summary>
	private string Text => Encoding.ASCII.GetString(_mBuffer.ToArray(), 0, Length);

	public override bool Equals(object obj)
	{
		if (ReferenceEquals(null, obj)) return false;
		if (ReferenceEquals(this, obj)) return true;
		if (obj.GetType() != GetType()) return false;
		return Equals((ByteBuffer)obj);
	}

	public override int GetHashCode() => HashCode.Combine(_mBuffer, _mReadPosition);

	public bool SaveData(string fileName, bool append = false)
	{
		try
		{
			var pathRoot = Path.GetDirectoryName(fileName);
			if (!Directory.Exists(pathRoot))
				Directory.CreateDirectory(pathRoot!);

			using var fileStream = new FileStream(
				fileName,
				append ? FileMode.Append : FileMode.Create,
				FileAccess.Write,
				FileShare.None
			);
			using var bw = new BinaryWriter(fileStream);
			bw.Write(Buffer);
			bw.Flush();
			bw.Close();
			fileStream.Flush();
			fileStream.Close();
		}
		catch
		{
			return false;
		}

		return true;
	}

	/// <summary>
	///     Clear current buffer
	/// </summary>
	public ByteBuffer Clear()
	{
		_mBuffer.Clear();
		ReadPosition = 0;
		return this;
	}

	/// <summary>
	///     Insert byte into buffer
	/// </summary>
	public ByteBuffer Insert(int start, byte @byte)
	{
		_mBuffer.Insert(start, @byte);
		return this;
	}

	/// <summary>
	///     Insert byte array into buffer
	/// </summary>
	public ByteBuffer Insert(int start, byte[] data)
	{
		_mBuffer.InsertRange(start, data);
		return this;
	}

	public ByteBuffer Remove2(int count)
	{
		// Count Check
		if (count < 0)
			return this;

		// Remove Data
		_mBuffer.RemoveRange(_mBuffer.Count - count, count);
		ReadPosition = 0;
		return this;
	}

	/// <summary>
	///     Remove 'Count' from 'Start' Bytes
	/// </summary>
	public ByteBuffer Remove(int start, int count)
	{
		if (count < 0)
			return this;

		_mBuffer.RemoveRange(0, Math.Min(count - start, Length - start));
		ReadPosition = 0;
		return this;
	}

	/// <summary>
	///     Read data from buffer into byte[]
	/// </summary>
	public void Read(byte[] data)
	{
		var count = Math.Min(data.Length, Length - ReadPosition);
		for (var i = 0; i < count; i++)
			data[i] = _mBuffer[ReadPosition++];
	}

	/// <summary>
	///     Read data from current buffer into new ByteBuffer
	/// </summary>
	public ByteBuffer Read(int count)
	{
		var data = new ByteBuffer();
		Read(data, count);
		return data;
	}

	/// <summary>
	///     Read Data from Buffer into ByteBuffer
	/// </summary>
	public void Read(ByteBuffer data, int count)
	{
		if (count < 1)
			return;

		count = Math.Min(count, Length - ReadPosition);
		for (var i = 0; i < count; i++)
			data.WriteByte(_mBuffer[ReadPosition++]);
	}

	/// <summary>
	///     Write Full Data to Buffer
	/// </summary>
	public void Write(byte[] data) => Write(data, data.Length);

	/// <summary>
	///     Write Full Data to Buffer
	/// </summary>
	public void Write(ReadOnlySpan<byte> data) => Write(data, data.Length);

	/// <summary>
	///     Write Data to Buffer (Count)
	/// </summary>
	public void Write(byte[] data, int count)
	{
		try
		{
			count = Math.Min(data.Length, count);
			for (var i = 0; i < count; i++)
				_mBuffer.Add(data[i]);
		}
		catch (Exception)
		{
			// Todo: Handle this
		}
	}

	/// <summary>
	///     Write Data to Buffer (Count)
	/// </summary>
	public void Write(ReadOnlySpan<byte> data, int count)
	{
		try
		{
			count = Math.Min(data.Length, count);
			for (var i = 0; i < count; i++)
				_mBuffer.Add(data[i]);
		}
		catch (Exception)
		{
			// Todo: Handle this
		}
	}

	/// <summary>
	///     Write ByteBuffer to buffer
	/// </summary>
	public void Write(ByteBuffer data) => Write(data.Buffer, data.Length);

	/// <summary>
	///     Write string to buffer
	/// </summary>
	public void Write(string data) => Write(Encoding.ASCII.GetBytes(data));

	/// <summary>
	///     Find Position of 'Character' in Buffer (Start Position: 0)
	/// </summary>
	public int IndexOf(char @char) => IndexOf(@char, 0);

	/// <summary>
	///     Find Position of 'Character' in Buffer
	/// </summary>
	public int IndexOf(char @char, int start)
	{
		if (start < 0 || start > Length)
			return -1;

		for (var i = start; i < Length; i++)
			if (_mBuffer[i] == (byte)@char)
				return i;

		return -1;
	}

	public byte ReadUByte()
	{
		var data = new byte[1];
		Read(data);
		return data[0];
	}

	public short ReadShort()
	{
		var data = new byte[2];
		Read(data);
		return (short)((data[0] << 8) + data[1]);
	}

	public int ReadInt()
	{
		var data = new byte[4];
		Read(data);
		return (data[0] << 24) + (data[1] << 16) + (data[2] << 8) + data[3];
	}

	public long ReadLong()
	{
		var data = new byte[8];
		Read(data);
		return ((long)data[0] << 56) +
		       ((long)data[1] << 48) +
		       ((long)data[2] << 40) +
		       ((long)data[3] << 32) +
		       ((long)data[4] << 24) +
		       ((long)data[5] << 16) +
		       ((long)data[6] << 8) +
		       data[7];
	}

	private string ReadCharsAsString(int pCount)
	{
		var data = new byte[pCount];
		Read(data);
		return Encoding.ASCII.GetString(data, 0, data.Length);
	}

	public ByteBuffer ReadChars(int pCount) => new ByteBuffer() + ReadCharsAsString(pCount);

	public ByteBuffer ReadChars2(uint pCount)
	{
		var data = new byte[pCount];
		Read(data);
		var data2 = new ByteBuffer();

		data2.Write(data, data.Length);

		return data2;
	}

	public ByteBuffer ReadDynamicString()
	{
		var resourceName      = new StringBuilder();
		var compressionOffset = -1;
		while (true)
		{
			// get segment length or detect termination of segments
			int segmentLength = _mBuffer[ReadPosition];

			// compressed name
			if ((segmentLength & 0xC0) == 0xC0)
			{
				ReadPosition++;
				if (compressionOffset == -1)
					// only record origin, and follow all pointers thereafter
					compressionOffset = ReadPosition;

				// move pointer to compression segment
				ReadPosition  = _mBuffer[ReadPosition];
				segmentLength = _mBuffer[ReadPosition];
			}

			if (segmentLength == 0x00)
			{
				if (compressionOffset != -1) ReadPosition = compressionOffset;
				// move past end of name \0
				ReadPosition++;
				break;
			}

			// move pass length and get segment text
			ReadPosition++;
			resourceName.Append($"{Encoding.Default.GetString(_mBuffer.ToArray(), ReadPosition, segmentLength)}.");
			ReadPosition += segmentLength;
		}

		return resourceName.ToString().TrimEnd(TrimChars);
	}

	public ByteBuffer WriteDynamicString(string str, char delimiter = '.')
	{
		str ??= "";

		var segments = str.Split(delimiter);
		foreach (var segment in segments)
		{
			WriteByte((byte)segment.Length);
			foreach (var currentChar in segment) WriteByte((byte)currentChar);
		}

		WriteByte(0x0);
		return this;
	}

	public ByteBuffer ReadString()
	{
		var data = new ByteBuffer();
		Read(data, Length - ReadPosition);
		return data;
	}

	public ByteBuffer ReadString(char item)
	{
		var pos  = IndexOf(item, ReadPosition) - ReadPosition;
		var data = new ByteBuffer();
		Read(data, pos >= 0 ? pos : Length - ReadPosition);
		ReadPosition++;
		return data;
	}

	public void WriteByte(byte @byte) => _mBuffer.Add(@byte);

	public void WriteByte(char @byte) => _mBuffer.Add((byte)@byte);

	public void WriteShort(short @byte)
	{
		var data = new byte[2];
		data[0] = (byte)((@byte >> 8) & 0xFF);
		data[1] = (byte)(@byte & 0xFF);
		Write(data);
	}

	public void WriteInt3(int @byte)
	{
		var data = new byte[3];
		data[0] = (byte)(@byte >> 0x10);
		data[1] = (byte)(@byte >> 0x8);
		data[2] = (byte)@byte;
		Write(data);
	}

	public void WriteInt(int @byte)
	{
		var data = new byte[4];
		data[0] = (byte)((@byte >> 24) & 0xFF);
		data[1] = (byte)((@byte >> 16) & 0xFF);
		data[2] = (byte)((@byte >> 8) & 0xFF);
		data[3] = (byte)(@byte & 0xFF);
		Write(data);
	}

	public void WriteLong(long @byte)
	{
		var data = new byte[5];
		data[0] = (byte)((@byte >> 56) & 0xFF);
		data[1] = (byte)((@byte >> 48) & 0xFF);
		data[2] = (byte)((@byte >> 40) & 0xFF);
		data[3] = (byte)((@byte >> 32) & 0xFF);
		data[4] = (byte)((@byte >> 24) & 0xFF);
		data[5] = (byte)((@byte >> 16) & 0xFF);
		data[6] = (byte)((@byte >> 8) & 0xFF);
		data[7] = (byte)(@byte & 0xFF);
		Write(data);
	}

	public sbyte  ReadByte()                                     => (sbyte)ReadUByte();
	public ushort ReadUShort()                                   => (ushort)ReadShort();
	public uint   ReadUInt()                                     => (uint)ReadInt();
	public ulong  ReadULong()                                    => (ulong)ReadLong();
	public void   WriteByte(sbyte pByte)                         => WriteByte((byte)pByte);
	public void   WriteShort(ushort pByte)                       => WriteShort((short)pByte);
	public void   WriteInt(uint pByte)                           => WriteInt((int)pByte);
	public void   WriteLong(ulong pByte)                         => WriteLong((long)pByte);
	public void   WriteByte<T>(T enumVal) where T : struct, Enum => WriteByte(Convert.ToByte(enumVal));

	public static implicit operator string(ByteBuffer d) => d.ToString();
	public static implicit operator ByteBuffer(string b) => new(b);
	public static implicit operator ByteBuffer(byte[] b) => new(b);

	public override string ToString() => Text;
	public          string ToLower()  => Text.ToLower();
	public          bool   ToBool()   => int.Parse(Text) == 1;

	public int ToInt()
	{
		_ = int.TryParse(Text, out var numbers);
		return numbers;
	}

	public bool IsEmpty() => Length == 0;
}