// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="DnsCliIntegrationCollection.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using Xunit;

namespace Dns.UnitTests.Integration;

[CollectionDefinition(Name)]
public sealed class DnsCliIntegrationCollection : ICollectionFixture<DnsCliHostFixture>
{
	public const string Name = "DnsCliIntegration";
}