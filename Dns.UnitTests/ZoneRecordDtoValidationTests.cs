using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Dns.Cli.Models.Dto;
using Dns.Db.Models.EntityFramework.Enums;
using Xunit;

namespace Dns.UnitTests;

public sealed class ZoneRecordDtoValidationTests
{
	[Theory]
	[InlineData(ResourceType.A, "2001:db8::10", "AAAA")]
	[InlineData(ResourceType.AAAA, "192.0.2.10", "A record")]
	[InlineData(ResourceType.A, "not-an-address", "valid IPv4")]
	[InlineData(ResourceType.AAAA, "not-an-address", "valid IPv6")]
	public void Validate_RejectsInvalidOrMismatchedAddress(ResourceType type, string data, string expectedMessage)
	{
		var record  = new ZoneRecordDto { Host = "www", Type = type, Data = data };
		var results = new List<ValidationResult>();

		var isValid = Validator.TryValidateObject(record, new(record), results, true);

		Assert.False(isValid);
		var result = Assert.Single(results);
		Assert.Contains(expectedMessage, result.ErrorMessage);
		Assert.Contains(nameof(ZoneRecordDto.Data), result.MemberNames);
	}

	[Theory]
	[InlineData(ResourceType.A, "192.0.2.10")]
	[InlineData(ResourceType.AAAA, "2001:db8::10")]
	[InlineData(ResourceType.CNAME, "target.example.com")]
	public void Validate_AcceptsMatchingAddressOrNonAddressRecord(ResourceType type, string data)
	{
		var record = new ZoneRecordDto { Host = "www", Type = type, Data = data };

		var isValid = Validator.TryValidateObject(record, new(record), null, true);

		Assert.True(isValid);
	}
}