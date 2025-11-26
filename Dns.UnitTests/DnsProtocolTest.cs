// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="DnsProtocolTest.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System.IO;
using System.Net;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Models.Dns.Packets;
using Dns.Models.Enums;
using Dns.RDataTypes;
using Dns.Serializers;
using Xunit;
using Xunit.Abstractions;

namespace Dns.UnitTests;

public class DnsProtocolTest
{
    public DnsProtocolTest(ITestOutputHelper testOutputHelper)
    {

    }
    [Fact]
    public void DnsQuery()
    {
        var sampleQuery = new byte[] {0xD3, 0x03, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x07, 0x64, 0x64, 0x63, 0x64, 0x73, 0x30, 0x31, 0x07, 0x72, 0x65, 0x64, 0x6D, 0x6F, 0x6E, 0x64, 0x04, 0x63, 0x6F, 0x72, 0x70, 0x09, 0x6D, 0x69, 0x63, 0x72, 0x6F, 0x73, 0x6F, 0x66, 0x74, 0x03, 0x63, 0x6F, 0x6D, 0x00, 0x00, 0x01, 0x00, 0x01};
        Assert.True(DnsMessage.TryParse(sampleQuery, out var query));
        query.Dump();
    }

    [Fact]
    public void DnsQuery2()
    {
        var sampleQuery = new byte[] {0x00, 0x03, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x77, 0x77, 0x77, 0x03, 0x6D, 0x73, 0x6E, 0x03, 0x63, 0x6F, 0x6D, 0x07, 0x72, 0x65, 0x64, 0x6D, 0x6F, 0x6E, 0x64, 0x04, 0x63, 0x6F, 0x72, 0x70, 0x09, 0x6D, 0x69, 0x63, 0x72, 0x6F, 0x73, 0x6F, 0x66, 0x74, 0x03, 0x63, 0x6F, 0x6D, 0x00, 0x00, 0x1C, 0x00, 0x01};
        Assert.True(DnsMessage.TryParse(sampleQuery, out var query));

        // Header Checks
        Assert.Equal(0x3, query.QueryIdentifier);
        Assert.False(query.QR);
        Assert.Equal(0x0000, query.Opcode);
        Assert.False(query.AA);
        Assert.False(query.TC);
        Assert.True(query.RD);
        Assert.False(query.RA);
        Assert.False(query.Zero);
        Assert.False(query.AuthenticatingData);
        Assert.False(query.CheckingDisabled);
        Assert.Equal(0x0000, query.RCode);
        Assert.Equal(0x0001, query.QuestionCount);
        Assert.Equal(0x0000, query.AnswerCount);
        Assert.Equal(0x0000, query.NameServerCount);
        Assert.Equal(0x0000, query.AdditionalCount);

        // Question Checks
        Assert.Equal(query.QuestionCount, query.Questions.Count);

        // Q1
        Assert.Equal("www.msn.com.redmond.corp.microsoft.com", query.Questions[0].Name);
        Assert.Equal(ResourceType.AAAA, query.Questions[0].Type);
        Assert.Equal(ResourceClass.IN, query.Questions[0].Class);

        // dump results
        query.Dump();
    }

    [Fact]
    public void DnsResponse1()
    {
        var sampleQuery = new byte[] {0x44, 0xFD, 0x81, 0x80, 0x00, 0x01, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x03, 0x77, 0x77, 0x77, 0x10, 0x67, 0x6F, 0x6F, 0x67, 0x6C, 0x65, 0x2D, 0x61, 0x6E, 0x61, 0x6C, 0x79, 0x74, 0x69, 0x63, 0x73, 0x03, 0x63, 0x6F, 0x6D, 0x00, 0x00, 0x01, 0x00, 0x01, 0xC0, 0x0C, 0x00, 0x05, 0x00, 0x01, 0x00, 0x00, 0x89, 0x89, 0x00, 0x20, 0x14, 0x77, 0x77, 0x77, 0x2D, 0x67, 0x6F, 0x6F, 0x67, 0x6C, 0x65, 0x2D, 0x61, 0x6E, 0x61, 0x6C, 0x79, 0x74, 0x69, 0x63, 0x73, 0x01, 0x6C, 0x06, 0x67, 0x6F, 0x6F, 0x67, 0x6C, 0x65, 0xC0, 0x21, 0xC0, 0x36, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x19, 0x00, 0x04, 0xAD, 0xC2, 0x21, 0x25, 0xC0, 0x36, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x19, 0x00, 0x04, 0xAD, 0xC2, 0x21, 0x21, 0xC0, 0x36, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x19, 0x00, 0x04, 0xAD, 0xC2, 0x21, 0x28, 0xC0, 0x36, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x19, 0x00, 0x04, 0xAD, 0xC2, 0x21, 0x29, 0xC0, 0x36, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x19, 0x00, 0x04, 0xAD, 0xC2, 0x21, 0x20, 0xC0, 0x36, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x19, 0x00, 0x04, 0xAD, 0xC2, 0x21, 0x2E, 0xC0, 0x36, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x19, 0x00, 0x04, 0xAD, 0xC2, 0x21, 0x26, 0xC0, 0x36, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x19, 0x00, 0x04, 0xAD, 0xC2, 0x21, 0x24, 0xC0, 0x36, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x19, 0x00, 0x04, 0xAD, 0xC2, 0x21, 0x27, 0xC0, 0x36, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x19, 0x00, 0x04, 0xAD, 0xC2, 0x21, 0x22, 0xC0, 0x36, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x19, 0x00, 0x04, 0xAD, 0xC2, 0x21, 0x23};
        Assert.True(DnsMessage.TryParse(sampleQuery, out var query));
        query.Dump();
    }

    [Fact]
    public void DnsMessage_Given_sample_query_Then_response_contains_compression_information()
    {
        //Arrange
        var sampleQuery = new byte[] {0x00, 0x04, 0x81, 0x80, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x03, 0x77, 0x77, 0x77, 0x03, 0x6D, 0x73, 0x6E, 0x03, 0x63, 0x6F, 0x6D, 0x00, 0x00, 0x01, 0x00, 0x01, 0xC0, 0x0C, 0x00, 0x05, 0x00, 0x01, 0x00, 0x00, 0x02, 0x35, 0x00, 0x1E, 0x02, 0x75, 0x73, 0x03, 0x63, 0x6F, 0x31, 0x03, 0x63, 0x62, 0x33, 0x06, 0x67, 0x6C, 0x62, 0x64, 0x6E, 0x73, 0x09, 0x6D, 0x69, 0x63, 0x72, 0x6F, 0x73, 0x6F, 0x66, 0x74, 0xC0, 0x14, 0xC0, 0x29, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x53, 0x00, 0x04, 0x83, 0xFD, 0x0D, 0x8C};

        //Act
        var result = DnsMessage.TryParse(sampleQuery, out var query);

        //Assert
        Assert.True(result);

        // Header Checks
        Assert.Equal(0x4, query.QueryIdentifier);
        Assert.True(query.QR);
        Assert.Equal(0x0000, query.Opcode);
        Assert.False(query.AA);
        Assert.False(query.TC);
        Assert.True(query.RD);
        Assert.True(query.RA);
        Assert.False(query.Zero);
        Assert.False(query.AuthenticatingData);
        Assert.False(query.CheckingDisabled);
        Assert.Equal(0x0000, query.RCode);
        Assert.Equal(0x0001, query.QuestionCount);
        Assert.Equal(0x0002, query.AnswerCount);
        Assert.Equal(0x0000, query.NameServerCount);
        Assert.Equal(0x0000, query.AdditionalCount);

        // Question Checks
        Assert.Equal(query.QuestionCount, query.Questions.Count);

        // Q1
        Assert.Equal("www.msn.com", query.Questions[0].Name);
        Assert.Equal(ResourceType.A, query.Questions[0].Type);
        Assert.Equal(ResourceClass.IN, query.Questions[0].Class);

        // dump results
        query.Dump();
    }

    // Response Contains Compression information
    [Fact]
    public void DnsResponse3()
    {
        var sampleQuery = new byte[] {0xDD, 0x15, 0x81, 0x80, 0x00, 0x01, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x03, 0x61, 0x70, 0x69, 0x04, 0x62, 0x69, 0x6E, 0x67, 0x03, 0x63, 0x6F, 0x6D, 0x00, 0x00, 0x01, 0x00, 0x01, 0xC0, 0x0C, 0x00, 0x05, 0x00, 0x01, 0x00, 0x00, 0x00, 0x83, 0x00, 0x14, 0x04, 0x61, 0x31, 0x33, 0x34, 0x02, 0x6C, 0x6D, 0x06, 0x61, 0x6B, 0x61, 0x6D, 0x61, 0x69, 0x03, 0x6E, 0x65, 0x74, 0x00, 0xC0, 0x2A, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x0E, 0x00, 0x04, 0xCF, 0x6D, 0x49, 0x91, 0xC0, 0x2A, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x0E, 0x00, 0x04, 0xCF, 0x6D, 0x49, 0x51};
        Assert.True(DnsMessage.TryParse(sampleQuery, out var query));

        // Header Checks
        Assert.Equal(0xDD15, query.QueryIdentifier);
        Assert.True(query.QR);
        Assert.Equal(0x0000, query.Opcode);
        Assert.False(query.AA);
        Assert.False(query.TC);
        Assert.True(query.RD);
        Assert.True(query.RA);
        Assert.False(query.Zero);
        Assert.False(query.AuthenticatingData);
        Assert.False(query.CheckingDisabled);
        Assert.Equal(0x0000, query.RCode);
        Assert.Equal(0x0001, query.QuestionCount);
        Assert.Equal(0x0003, query.AnswerCount);
        Assert.Equal(0x0000, query.NameServerCount);
        Assert.Equal(0x0000, query.AdditionalCount);

        // Question Checks
        Assert.Equal(query.QuestionCount, query.Questions.Count);

        // Q1
        Assert.Equal("api.bing.com", query.Questions[0].Name);
        Assert.Equal(ResourceType.A, query.Questions[0].Type);
        Assert.Equal(ResourceClass.IN, query.Questions[0].Class);

        // Answer Checks
        Assert.Equal(query.AnswerCount, query.Answers.Count);

        // A1
        Assert.Equal("api.bing.com", query.Answers[0].Name);
        Assert.Equal(ResourceType.CNAME, query.Answers[0].Type);
        Assert.Equal(ResourceClass.IN, query.Answers[0].Class);
        Assert.True(query.Answers[0].TTL == 0x83);
        Assert.Equal(0x14, query.Answers[0].DataLength);
        Assert.Equal(typeof (CNameRData), query.Answers[0].RData.GetType());

        // dump results
        query.Dump();
    }

    // Response Contains Compression information
    [Fact]
    public void DnsQuery3()
    {
        var sampleQuery = new byte[] {0xFB, 0x65, 0x84, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0x73, 0x65, 0x63, 0x75, 0x72, 0x65, 0x2D, 0x75, 0x73, 0x0C, 0x69, 0x6D, 0x72, 0x77, 0x6F, 0x72, 0x6C, 0x64, 0x77, 0x69, 0x64, 0x65, 0x03, 0x63, 0x6F, 0x6D, 0x00, 0x00, 0x1C, 0x00, 0x01};
        Assert.True(DnsMessage.TryParse(sampleQuery, out var query));

        // Header Checks
        Assert.Equal(0xFB65, query.QueryIdentifier);
        Assert.True(query.QR);
        Assert.Equal(0x0000, query.Opcode);
        Assert.True(query.AA);
        Assert.False(query.TC);
        Assert.False(query.RD);
        Assert.False(query.RA);
        Assert.False(query.Zero);
        Assert.False(query.AuthenticatingData);
        Assert.False(query.CheckingDisabled);
        Assert.Equal(0x0000, query.RCode);
        Assert.Equal(0x0001, query.QuestionCount);
        Assert.Equal(0x0000, query.AnswerCount);
        Assert.Equal(0x0000, query.NameServerCount);
        Assert.Equal(0x0000, query.AdditionalCount);

        // Question Checks
        Assert.Equal(query.QuestionCount, query.Questions.Count);

        // Q1
        Assert.Equal("secure-us.imrworldwide.com", query.Questions[0].Name);
        Assert.Equal(ResourceType.AAAA, query.Questions[0].Type);
        Assert.Equal(ResourceClass.IN, query.Questions[0].Class);

        // dump results
        query.Dump();
    }

    [Fact(Skip = "Will fix later")]
    public void SerializerTest()
    {
        var       question = new Question(name: "www.msn.com", pClass: ResourceClass.IN, type: ResourceType.A);
        using var stream   = new MemoryStream();
        question.WriteToStream(stream);

        var streamBuffer = stream.GetBuffer();
        var serializerBuffer = question.Serialize().Buffer.ToArray();


        var ret = ((ByteBuffer)streamBuffer).Deserialize<Question>();
        Assert.Equal(streamBuffer, serializerBuffer);
    }

    [Fact]
    public void TransitiveQueryTest()
    {
        //Arrange
        var message = new DnsMessage { QueryIdentifier = 0xFEED, QR = false, Opcode = (byte)OpCode.QUERY, AA = false,
            TC = false,
            RD = true,
            RA = false,
            Zero = false,
            AuthenticatingData = false,
            CheckingDisabled = false,
            RCode = 0x0000,
            QuestionCount = 1,
            AnswerCount = 0,
            NameServerCount = 0,
            AdditionalCount = 0,
            Questions = [new("www.msn.com", ResourceType.A, ResourceClass.IN)],
        };

        using var  stream = new MemoryStream();
        message.WriteToStream(stream);

        //Act
        var result = DnsMessage.TryParse(stream.GetBuffer(), out var outMessage);

        //Assert
        Assert.True(result);
        Assert.Equal(0xFEED, outMessage.QueryIdentifier);
        Assert.False(outMessage.QR);
        Assert.Equal((byte) OpCode.QUERY, outMessage.Opcode);
        Assert.False(outMessage.AA);
        Assert.False(outMessage.TC);
        Assert.True(outMessage.RD);
        Assert.False(outMessage.RA);
        Assert.False(outMessage.Zero);
        Assert.False(outMessage.AuthenticatingData);
        Assert.False(outMessage.CheckingDisabled);
        Assert.Equal(0x0000, outMessage.RCode);
        Assert.Equal(0x0001, outMessage.QuestionCount);
        Assert.Equal(0x0000, outMessage.AnswerCount);
        Assert.Equal(0x0000, outMessage.NameServerCount);
        Assert.Equal(0x0000, outMessage.AdditionalCount);

        // Question Checks
        Assert.Equal(outMessage.QuestionCount, outMessage.Questions.Count);

        // Q1
        Assert.Equal("www.msn.com", outMessage.Questions[0].Name);
        Assert.Equal(ResourceType.A, outMessage.Questions[0].Type);
        Assert.Equal(ResourceClass.IN, outMessage.Questions[0].Class);
    }

    [Fact]
    public void TransitiveQueryTest2()
    {
        var message = new DnsMessage { QueryIdentifier = 0xFEED, QR = false, Opcode = (byte)OpCode.QUERY, AA = false,
            TC = false,
            RD = true,
            RA = false,
            Zero = false,
            AuthenticatingData = false,
            CheckingDisabled = false,
            RCode = 0x0000,
            QuestionCount = 1,
            AnswerCount = 2,
            NameServerCount = 0,
            AdditionalCount = 0,
            Questions = [new("www.msn.com", ResourceType.A, ResourceClass.IN)],
        };
        message.Answers.Add(new() {Name = "8.8.8.8", Class = ResourceClass.IN, Type = ResourceType.NS, TTL = 468, DataLength = 0, RData = null});
        var data = new ANameRData {Address = IPAddress.Parse("8.8.8.9")};
        message.Answers.Add(new() {Name = "8.8.8.9", Class = ResourceClass.IN, Type = ResourceType.NS, TTL = 468, RData = data, DataLength = data.Length});

        DnsMessage outMessage;
        using (var stream = new MemoryStream())
        {
            message.WriteToStream(stream);
            Assert.True(DnsMessage.TryParse(stream.GetBuffer(), out outMessage));
        }

        Assert.Equal(0xFEED, outMessage.QueryIdentifier);
        Assert.False(outMessage.QR);
        Assert.Equal((byte) OpCode.QUERY, outMessage.Opcode);
        Assert.False(outMessage.AA);
        Assert.False(outMessage.TC);
        Assert.True(outMessage.RD);
        Assert.False(outMessage.RA);
        Assert.False(outMessage.Zero);
        Assert.False(outMessage.AuthenticatingData);
        Assert.False(outMessage.CheckingDisabled);
        Assert.Equal(0x0000, outMessage.RCode);
        Assert.Equal(0x0001, outMessage.QuestionCount);
        Assert.Equal(0x0002, outMessage.AnswerCount);
        Assert.Equal(0x0000, outMessage.NameServerCount);
        Assert.Equal(0x0000, outMessage.AdditionalCount);

        // Question Checks
        Assert.Equal(outMessage.QuestionCount, outMessage.Questions.Count);

        // Q1
        Assert.Equal("www.msn.com", outMessage.Questions[0].Name);
        Assert.Equal(ResourceType.A, outMessage.Questions[0].Type);
        Assert.Equal(ResourceClass.IN, outMessage.Questions[0].Class);

        Assert.Equal(outMessage.AnswerCount, outMessage.Answers.Count);
        Assert.Equal(outMessage.AnswerCount, outMessage.Answers.Count);
        Assert.Equal("8.8.8.8", outMessage.Answers[0].Name);
        Assert.Equal("8.8.8.9", outMessage.Answers[1].Name);
    }

    [Fact]
    public void Opcode()
    {
        var message = new DnsMessage { QR = true };
        Assert.Equal(0x8000, message.Flags);
        message.Opcode = (byte) OpCode.UPDATE;
        Assert.Equal((byte) OpCode.UPDATE, message.Opcode);
        Assert.Equal(0xa800, message.Flags);
    }
}