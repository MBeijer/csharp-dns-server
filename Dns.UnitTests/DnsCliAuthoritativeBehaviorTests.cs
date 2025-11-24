// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="DnsCliAuthoritativeBehaviorTests.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Models.Enums;
using Dns.RDataTypes;
using Dns.UnitTests.Integration;
using Xunit;

namespace Dns.UnitTests;

[Collection(DnsCliIntegrationCollection.Name)]
public sealed class DnsCliAuthoritativeBehaviorTests(DnsCliHostFixture fixture)
{
    private static readonly IPAddress PrimaryHostAddress = IPAddress.Parse("192.0.2.10");
    private static readonly List<IPAddress> RoundRobinAddresses =
    [
        IPAddress.Parse("192.0.2.11"),
        IPAddress.Parse("192.0.2.12"),
        IPAddress.Parse("192.0.2.13"),
    ];

    [Fact]
    public async Task InZoneQueriesReturnAuthoritativeAnswers()
    {
        var hostName = fixture.BuildHostName("alpha");
        var response = await fixture.Client.QueryAsync(hostName);

        Assert.True(response.QR);
        Assert.True(response.AA);
        Assert.False(response.RA);
        Assert.Equal(RCode.NOERROR, (RCode)response.RCode);
        Assert.Equal((ushort)1, response.AnswerCount);

        var answer = Assert.Single(response.Answers);
        Assert.Equal(hostName, answer.Name);
        Assert.Equal(ResourceType.A, answer.Type);
        Assert.Equal(ResourceClass.IN, answer.Class);
        Assert.Equal((uint)10, answer.TTL);
        var address = Assert.IsType<ANameRData>(answer.RData);
        Assert.Equal(PrimaryHostAddress, address.Address);
    }

    [Fact]
    public async Task RecursionDesiredFlagDoesNotGrantRecursionAvailability()
    {
        var hostName = fixture.BuildHostName("alpha");
        var response = await fixture.Client.QueryAsync(hostName, recursionDesired: true);

        Assert.True(response.RD);
        Assert.False(response.RA);
        Assert.True(response.AA);
    }

    [Fact]
    public async Task RoundRobinHostsRotateAddressesAcrossQueries()
    {
        var hostName     = fixture.BuildHostName("round");
        var response     = await fixture.Client.QueryAsync(hostName);
        var firstAnswers = response.Answers.Select(responseAnswer => Assert.IsType<ANameRData>(responseAnswer.RData).Address).ToList();

        Assert.Equal(RoundRobinAddresses, firstAnswers);
    }

    [Fact]
    public async Task PositiveResponsesKeepConfiguredTtl()
    {
        var hostName = fixture.BuildHostName("alpha");

        var firstResponse  = await fixture.Client.QueryAsync(hostName);
        var secondResponse = await fixture.Client.QueryAsync(hostName);

        Assert.Equal((uint)10, Assert.Single(firstResponse.Answers).TTL);
        Assert.Equal((uint)10, Assert.Single(secondResponse.Answers).TTL);
        Assert.True(firstResponse.AA);
        Assert.True(secondResponse.AA);
    }

    [Fact]
    public async Task NonexistentHostsReturnSoaAuthorityWithMinimumTtl()
    {
        var missingHost = fixture.BuildHostName("missing");

        var firstResponse  = await fixture.Client.QueryAsync(missingHost);
        var secondResponse = await fixture.Client.QueryAsync(missingHost);

        Assert.Equal(RCode.NXDOMAIN, (RCode)firstResponse.RCode);
        Assert.Equal((ushort)0, firstResponse.AnswerCount);
        Assert.Equal((ushort)1, firstResponse.NameServerCount);
        Assert.True(firstResponse.AA);
        Assert.False(firstResponse.RA);

        var soaRecord = Assert.Single(firstResponse.Authorities);
        Assert.Equal(ResourceType.SOA, soaRecord.Type);
        Assert.Equal((uint)300, soaRecord.TTL);
        var soaData = Assert.IsType<SOARData>(soaRecord.RData);
        Assert.Equal((uint)300, soaData.MinimumTTL);

        var secondSoaRecord = Assert.Single(secondResponse.Authorities);
        Assert.Equal((uint)300, secondSoaRecord.TTL);
        var secondSoaData = Assert.IsType<SOARData>(secondSoaRecord.RData);
        Assert.Equal((uint)300, secondSoaData.MinimumTTL);
    }
}