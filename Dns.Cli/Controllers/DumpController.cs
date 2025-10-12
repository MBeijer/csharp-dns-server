using System;
using System.IO;
using Dns.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Dns.Cli.Controllers;

/// <summary>
///
/// </summary>
/// <param name="dnsService"></param>
/// <param name="dnsServer"></param>
[ApiController]
[Route("dump/")]
[Obsolete("Only available for backwards compatibility")]
public class DumpController(IDnsService dnsService, IDnsServer dnsServer) : ControllerBase
{
	/// <summary>
	///     Dump Resolver data
	/// </summary>
	/// <returns>html</returns>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[Produces("text/html")]
	[HttpGet("dnsresolver")]
	[Obsolete("Only available for backwards compatibility")]
	public IActionResult? GetDnsResolverData()
	{
		using var writer = new StringWriter();
		foreach (var zoneResolver in dnsService.Resolvers)
			zoneResolver.DumpHtml(writer);

		return Content(writer.ToString(), "text/html");
	}

	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[Produces("text/html")]
	[HttpGet("dnsserver")]
	[Obsolete("Only available for backwards compatibility")]
	public IActionResult? GetDnsServerData()
	{
		using var writer = new StringWriter();
		dnsServer.DumpHtml(writer);

		return Content(writer.ToString(), "text/html");
	}
}