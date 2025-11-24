using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Dns.Models.Dns.Packets;
using Dns.Serializers.Attributes;

namespace Dns.Serializers;

public static class PacketSerializer
{
	public static T Deserialize<T>(this ByteBuffer packet) where T : GenericPacket
	{
		var ctor = typeof(T).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
		                    .OrderByDescending(c => c.GetParameters().Length)
		                    .FirstOrDefault() ??
		           throw new InvalidOperationException($"Type {typeof(T).Name} must have a public constructor.");

		var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

		var args = new object[ctor.GetParameters().Length];

		for (var i = 0; i < args.Length; i++)
		{
			if (packet.BytesLeft <= 0) continue;

			var pInfo = ctor.GetParameters()[i];
			var pType = pInfo.ParameterType;
			var pProp = FindPropForParam(pInfo);

			var underlying = Nullable.GetUnderlyingType(pType) ?? pType;
			args[i] = ReadValue(underlying, pProp);
		}

		return (T)ctor.Invoke(args);

		// === helpers ===

		PropertyInfo FindPropForParam(ParameterInfo p) =>
			props.FirstOrDefault(pi => string.Equals(pi.Name, p.Name, StringComparison.OrdinalIgnoreCase));

		object ReadValue(Type t, PropertyInfo pProp)
		{
			// 1) Primitives + string
			switch (Type.GetTypeCode(t))
			{
				case TypeCode.Boolean: return packet.ReadUByte() == 1;
				case TypeCode.Byte:    return packet.ReadUByte();
				case TypeCode.SByte:   return packet.ReadByte();
				case TypeCode.UInt16:  return packet.ReadUShort();
				case TypeCode.Int16:   return packet.ReadShort();
				case TypeCode.UInt32:  return packet.ReadUInt();
				case TypeCode.Int32:   return packet.ReadInt();
				case TypeCode.UInt64:  return packet.ReadULong();
				case TypeCode.Int64:   return packet.ReadLong();

				case TypeCode.String:
				{
					if (pProp?.GetCustomAttribute<FixedLengthStringAttribute>() is { } fixedLen)
						return packet.ReadChars2(fixedLen.Length).ToString();

					if (pProp?.GetCustomAttribute<DynamicLengthStringAttribute>() is { } dyn)
						return packet.ReadDynamicString().ToString();

					return packet.ReadString().ToString();
				}
			}

			if (t == typeof(ByteBuffer))
				return packet.ReadString();

			if (t.IsEnum)
			{
				var ut  = Enum.GetUnderlyingType(t);
				var raw = ReadValue(ut, pProp)!;
				var num = Convert.ChangeType(raw, ut, CultureInfo.InvariantCulture)!;
				return Enum.ToObject(t, num);
			}
/*
			if (IsGenericEnumerable(t, out var elemType))
			{
				if (elemType == typeof(string))
				{
					var list = (IList)Activator.CreateInstance(typeof(List<string>))!;

					if (pProp?.GetCustomAttribute<GStringListAttribute>() is not null)
					{
						// If you also tag with [CalculateArray], read a byte count first
						var count = pProp.GetCustomAttribute<CalculateArrayAttribute>() is not null
							? packet.ReadGuByte1()
							: ReadUntilEndCount(packet); // fallback: consume until packet end

						for (var i = 0; i < count; i++)
							list.Add(packet.ReadGString().ToString());

						return list;
					}

					// Tokenized representation (inverse of IEnumerable<string>.Tokenize()).
					// If you already have a matching extension, use it here.
					var tokenized = packet.ReadString().ToString();
					var items     = DeTokenize(tokenized); // implement inverse of Tokenize()
					foreach (var s in items) list.Add(s);
					return list;
				}

				// SubPacket list
				if (typeof(SubPacket).IsAssignableFrom(elemType))
				{
					var count = pProp?.GetCustomAttribute<CalculateArrayAttribute>() is not null
						? packet.ReadGuByte1()
						: ReadUntilEndCount(packet); // optional fallback if no count prefix

					var listType = typeof(List<>).MakeGenericType(elemType);
					var list     = (IList)Activator.CreateInstance(listType)!;

					for (var i = 0; i < count; i++)
						list.Add(ReadSubPacket(elemType, packet));

					return list;
				}

				// Primitive list (optional): support if you need it later.
				if (Type.GetTypeCode(elemType) != TypeCode.Object)
				{
					var count = pProp?.GetCustomAttribute<CalculateArrayAttribute>() is not null
						? packet.ReadGuByte1()
						: ReadUntilEndCount(packet);

					var listType = typeof(List<>).MakeGenericType(elemType);
					var list     = (IList)Activator.CreateInstance(listType)!;

					for (var i = 0; i < count; i++)
						list.Add(ReadValue(elemType, pProp)); // reuse primitive reader

					return list;
				}

				throw new NotSupportedException($"Unsupported enumerable element type: {elemType.FullName}");
			}

			// 4) SubPacket object
			if (typeof(SubPacket).IsAssignableFrom(t))
				return ReadSubPacket(t, packet);
*/
			throw new NotSupportedException($"Unsupported parameter type: {t.FullName}");
		}
/*
		static bool IsGenericEnumerable(Type t, out Type elemType)
		{
			elemType = null!;
			if (!typeof(IEnumerable).IsAssignableFrom(t)) return false;
			if (!t.IsGenericType) return false;
			var args = t.GetGenericArguments();
			if (args.Length != 1) return false;
			elemType = args[0];
			return true;
		}

		static int ReadUntilEndCount(ByteBuffer pkt) =>
			// In protocols where the list is "until packet end",
			// this helper lets you estimate by consuming in a loop in the caller.
			// Here we just return int.MaxValue to let the caller read until BytesLeft == 0.
			// Callers must cap by BytesLeft.
			int.MaxValue;

		static IEnumerable<string> DeTokenize(string tokenized) => string.IsNullOrEmpty(tokenized) ? [] : tokenized.Detokenize();

		object ReadSubPacket(Type subType, ByteBuffer pkt)
		{
			var subCtor = subType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
			                     .OrderByDescending(c => c.GetParameters().Length)
			                     .FirstOrDefault();

			var subProps = subType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			                      .Where(pi => pi.GetIndexParameters().Length == 0)
			                      .ToArray();

			if (subType.GetCustomAttribute<CalculatePropsAttribute>() is not null)
			{
				pkt.ReadGuByte1();
			}


			// If it has a "primary" constructor, respect its parameter order
			if (subCtor is not null && subCtor.GetParameters().Length > 0)
			{
				var subArgs = new object?[subCtor.GetParameters().Length];
				for (var i = 0; i < subArgs.Length; i++)
				{
					var p = subCtor.GetParameters()[i];
					var pp = subProps.FirstOrDefault(pi => string.Equals(
						                                 pi.Name,
						                                 p.Name,
						                                 StringComparison.OrdinalIgnoreCase
					                                 )
					);
					var pt = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
					subArgs[i] = ReadValue(pt, pp);
				}

				return subCtor.Invoke(subArgs);
			}

			// Otherwise, parameterless: read in property order and set values
			var instance = Activator.CreateInstance(subType)!;
			foreach (var pi in subProps)
			{
				if (!pi.CanWrite) continue; // skip get-only
				var vt = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
				var vv = ReadValue(vt, pi);
				pi.SetValue(instance, vv);
			}

			return instance;
		}
		*/
	}

	public static ByteBuffer Serialize<T>(this T packet) where T : GenericPacket
	{
		var output = new ByteBuffer();

		var type = packet.GetType();
/*
		if (!type.IsSubclassOf(typeof(SubPacket)) && !newProtocol)
		{
			var packetIdAttr = type.GetCustomAttribute<PacketIdAttribute>();
			if (packetIdAttr == null)
				throw new InvalidOperationException("Missing ClientPacketId attribute.");

			var ut = Enum.GetUnderlyingType(packetIdAttr.Id.GetType());
			output.WriteGByte1((byte)Convert.ChangeType(packetIdAttr.Id, ut));
		}
*/
		var properties = type.GetProperties().Where(p => p.GetValue(packet) != null).ToList();

		//if (type.GetCustomAttribute<CalculatePropsAttribute>() is not null) output.WriteGByte1((byte)properties.Count);

		foreach (var prop in properties)
		{
			var val = prop.GetValue(packet);

			if (val is null)
				continue;

			var propertyType = prop.PropertyType;
			if (propertyType.IsEnum)
			{
				var underlyingType = Enum.GetUnderlyingType(propertyType);
				val          = Convert.ChangeType(val, underlyingType);
				propertyType = underlyingType;
			}

			switch (val)
			{
				case bool boolValue:
					output.WriteByte((byte)(boolValue ? 1 : 0));
					break;
				case byte bval:
					output.WriteByte(bval);
					break;
				case sbyte bval:
					output.WriteByte(bval);
					break;
				case short sval:
					output.WriteShort(sval);
					break;
				case ushort sval:
					output.WriteShort(sval);
					break;
				case int ival:
					output.WriteInt(ival);
					break;
				case uint ival:
					output.WriteInt(ival);
					break;
				case long lval:
					output.WriteLong(lval);
					break;
				case ulong lval:
					output.WriteLong(lval);
					break;
				case ByteBuffer gByteBuffer:
					output.Write(gByteBuffer);
					break;
				/*
				case IEnumerable<string> enumerableString:
					if (prop.GetCustomAttribute<GStringListAttribute>() is not null)
					{
						foreach (var es in enumerableString) output.WriteGString(es);

						break;
					}

					output.Write(enumerableString.Tokenize());
					break;
				case IEnumerable<SubPacket> enumerableSubPacket:
					var enumerable = enumerableSubPacket.ToList();
					if (prop.GetCustomAttribute<CalculateArrayAttribute>() is not null)
						output.WriteGByte1((byte)enumerable.Count);

					foreach (var subPacket in enumerable) output.Write(subPacket.Serialize());

					break;
				*/
				case string:
				{
					if (prop.GetCustomAttribute<FixedLengthStringAttribute>() is { } graalStringFixed)
					{
						//output.WriteGByte1((byte)graalStringFixed.Length);
						var stringVal = val?.ToString() ?? "";

						while (stringVal.Length < graalStringFixed.Length)
							stringVal += ' ';

						output.Write(stringVal);

						break;
					}

					if (prop.GetCustomAttribute<DynamicLengthStringAttribute>() is not null)
					{
						var outputString = val?.ToString() ?? "";
						output.WriteDynamicString(outputString);
						break;
					}

					output.Write(val?.ToString() ?? "");

					break;
				}
				default:
				{
					/*
					if (val!.GetType().GetCustomAttribute<TokenizeAttribute>() is not null)
					{
						var subType = val.GetType();
						var stringList = subType.GetProperties()
						                        .Select(subProp => subProp.GetValue(val))
						                        .OfType<object>()
						                        .Select(subVal => subVal?.ToString() ?? "")
						                        .ToList();

						output.Write(stringList.Tokenize());
					}
					*/

					break;
				}
			}
		}

		return output;
	}
}