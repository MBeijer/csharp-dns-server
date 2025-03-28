﻿// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="Extensions.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dns;

public static class Extensions
{
    public static TextWriter CreateWriter(this Stream stream, Encoding encoding = null)
    {
        encoding ??= Encoding.UTF8;
        // write all data using UTF-8
        return new StreamWriter(stream, encoding);
    }

    public static ushort SwapEndian(this ushort val)
    {
        var value = (ushort) ((val << 8) | (val >> 8));
        return value;
    }

    public static uint SwapEndian(this uint val)
    {
        var value = (val << 24) | ((val << 8) & 0x00ff0000) | ((val >> 8) & 0x0000ff00) | (val >> 24);
        return value;
    }

    private static readonly char[] separator = new[] {'.'};

    public static byte[] GetResourceBytes(this string str, char delimiter = '.')
    {
        str ??= "";

        using var stream = new MemoryStream(str.Length + 2);
        var segments = str.Split(separator);
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

        // null delimiter
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


    public static byte[] GetBytes(this string str, Encoding encoding = null)
    {
        encoding ??= Encoding.ASCII;
        return encoding.GetBytes(str);
    }

    public static string IP(long ipLong)
    {
        var b = new StringBuilder();
        long tempLong, temp;

        tempLong = ipLong;
        temp = tempLong/(256*256*256);
        tempLong -= (temp*256*256*256);
        b.Append(Convert.ToString(temp)).Append(".");
        temp = tempLong/(256*256);
        tempLong -= (temp*256*256);
        b.Append(Convert.ToString(temp)).Append(".");
        temp = tempLong/256;
        tempLong -= (temp*256);
        b.Append(Convert.ToString(temp)).Append(".");
        temp = tempLong;
        tempLong -= temp;
        b.Append(Convert.ToString(temp));

        return b.ToString().ToLower();
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