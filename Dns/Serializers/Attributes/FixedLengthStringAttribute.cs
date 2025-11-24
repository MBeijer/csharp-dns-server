using System;

namespace Dns.Serializers.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class FixedLengthStringAttribute(uint length) : Attribute
{
	public uint Length { get; } = length;
}