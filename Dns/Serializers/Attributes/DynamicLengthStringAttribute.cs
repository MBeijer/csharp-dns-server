using System;

namespace Dns.Serializers.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class DynamicLengthStringAttribute(TypeCode lengthType = TypeCode.Byte) : Attribute
{
	public TypeCode LengthType { get; } = lengthType;
}