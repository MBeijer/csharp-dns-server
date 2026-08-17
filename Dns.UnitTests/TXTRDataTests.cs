// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="TXTRDataTests.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.IO;
using Dns.RDataTypes;
using Xunit;

namespace Dns.UnitTests;

public class TXTRDataTests
{
	[Fact]
	public void RoundTrip_DoesNotInterpretLongTextAsDnsLabelsOrCompressionPointers()
	{
		var       expected = new string('x', 200);
		var       rData    = new TXTRData { Name = expected };
		using var stream   = new MemoryStream();

		rData.WriteToStream(stream);
		var payload = stream.ToArray();
		var parsed  = TXTRData.Parse(payload, 0, payload.Length);

		Assert.Equal(200, payload[0]);
		Assert.Equal(expected, parsed.Name);
		Assert.Equal(payload.Length, rData.Length);
	}

	[Fact]
	public void RoundTrip_SplitsTextAcrossMultipleCharacterStrings()
	{
		var       expected = new string('x', 600);
		var       rData    = new TXTRData { Name = expected };
		using var stream   = new MemoryStream();

		rData.WriteToStream(stream);
		var payload = stream.ToArray();
		var parsed  = TXTRData.Parse(payload, 0, payload.Length);

		Assert.Equal(255, payload[0]);
		Assert.Equal(255, payload[256]);
		Assert.Equal(90, payload[512]);
		Assert.Equal(expected, parsed.Name);
		Assert.Equal(603, rData.Length);
	}

	[Fact]
	public void Parse_RejectsCharacterStringOutsideRdataBoundary()
	{
		var payload = new byte[] { 5, (byte)'a' };

		Assert.Throws<InvalidDataException>(() => TXTRData.Parse(payload, 0, payload.Length));
	}
}