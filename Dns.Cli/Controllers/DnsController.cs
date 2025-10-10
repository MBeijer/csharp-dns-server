using System;
using System.IO;
using System.Linq;
using Dns.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Dns.Cli.Controllers;

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
	[Obsolete("Only available for backwards compatibility")]
	public IActionResult? GetDnsResolverData()
	{
		using var writer = new StringWriter();
		foreach (var zoneResolver in dnsService.Resolvers)
			zoneResolver.DumpHtml(writer);

		return Ok(dnsService.Resolvers.Select(s => s.GetObject()));
	}
}