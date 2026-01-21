using System.Linq;
using System.Threading.Tasks;
using Dns.Db.Models.EntityFramework;
using Dns.Db.Repositories;
using Dns.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Dns.Cli.Controllers;

#pragma warning disable CS9113

/// <summary>
/// </summary>
/// <param name="dnsService"></param>
/// <param name="dnsServer"></param>
/// <param name="zoneRepository"></param>
[ApiController]
[Route("dns/")]
public class DnsController(IDnsService dnsService, IDnsServer dnsServer, IZoneRepository zoneRepository)
	: ControllerBase
{
	/// <summary>
	///     Dump Resolver data
	/// </summary>
	/// <returns>html</returns>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpGet("resolvers")]
	public IActionResult? GetDnsResolverData() => Ok(dnsService.Resolvers.Select(s => s.GetObject()));

	/// <summary>
	///     Get database zones
	/// </summary>
	/// <returns>html</returns>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpGet("zones")]
	public async Task<IActionResult?> GetZones() => Ok(await zoneRepository.GetZones().ConfigureAwait(false));

	/// <summary>
	///     Get database zones
	/// </summary>
	/// <returns>html</returns>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpPut("zones")]
	public async Task<IActionResult?> AddZone([FromBody] Zone zone)
	{
		await zoneRepository.AddZone(zone).ConfigureAwait(false);
		return Created();
	}

	/// <summary>
	///     Get database zones
	/// </summary>
	/// <returns>html</returns>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpPatch("zones")]
	public async Task<IActionResult?> UpdateZone([FromBody] Zone zone)
	{
		await zoneRepository.UpdateZone(zone).ConfigureAwait(false);
		return Ok();
	}
}

#pragma warning restore CS9113