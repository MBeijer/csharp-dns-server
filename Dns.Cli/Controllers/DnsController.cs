using System.Linq;
using Dns.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Dns.Cli.Controllers;

#pragma warning disable CS9113

[ApiController]
[Route("dns/")]
public class DnsController(IDnsService dnsService, IDnsServer dnsServer) : ControllerBase
{
	/// <summary>
	///     Dump Resolver data
	/// </summary>
	/// <returns>html</returns>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpGet("resolvers")]
	public IActionResult? GetDnsResolverData()
		=> Ok(dnsService.Resolvers.Select(s => s.GetObject()));
}

#pragma warning restore CS9113