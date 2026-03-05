using System;
using System.Linq;
using System.Threading.Tasks;
using Dns.Cli.Models;
using Dns.Db.Models.EntityFramework;
using Dns.Db.Repositories;
using Dns.Services;
using Dns.ZoneProvider.Bind;
using Microsoft.AspNetCore.Authorization;
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
	[Authorize]
	public IActionResult? GetDnsResolverData() => Ok(dnsService.Resolvers.Select(s => s.GetObject()));

	/// <summary>
	///     Get database zones
	/// </summary>
	/// <returns>html</returns>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpGet("zones")]
	[Authorize]
	public async Task<IActionResult?> GetZones() => Ok(await zoneRepository.GetZones().ConfigureAwait(false));

	/// <summary>
	///     Get database zones
	/// </summary>
	/// <returns>html</returns>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpPut("zones")]
	[Authorize]
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
	[Authorize]
	public async Task<IActionResult?> UpdateZone([FromBody] Zone zone)
	{
		await zoneRepository.UpdateZone(zone).ConfigureAwait(false);
		return Ok();
	}

	/// <summary>
	///     Import a BIND zone file into the database-backed zone model.
	/// </summary>
	/// <param name="request">Import settings.</param>
	/// <returns>Imported zone summary.</returns>
	[ProducesResponseType(StatusCodes.Status201Created)]
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpPost("zones/import-bind")]
	[Authorize]
	public async Task<IActionResult?> ImportBindZone([FromBody] BindZoneImportRequest request)
	{
		if (!ModelState.IsValid) return ValidationProblem(ModelState);

		if (!System.IO.File.Exists(request.FileName))
			return BadRequest($"Zone file was not found: {request.FileName}");

		Dns.Models.Zone parsedZone;
		try
		{
			parsedZone = BindZoneProvider.ParseZoneFile(request.FileName, request.ZoneSuffix);
		}
		catch (Exception ex)
		{
			return BadRequest($"Unable to parse BIND zone file: {ex.Message}");
		}

		var dbZone = BindZoneImportMapper.ToDbZone(parsedZone, request.ZoneSuffix, request.Enabled);

		var existing = await zoneRepository.GetZone(dbZone.Suffix!).ConfigureAwait(false);
		var upserted = await zoneRepository.UpsertZone(dbZone, request.ReplaceExistingRecords).ConfigureAwait(false);

		var payload = new
		{
			id          = upserted.Id,
			suffix      = upserted.Suffix,
			serial      = upserted.Serial,
			enabled     = upserted.Enabled,
			recordCount = upserted.Records?.Count ?? dbZone.Records?.Count ?? 0,
		};

		if (existing == null) return Created($"/dns/zones/{upserted.Id}", payload);

		return Ok(payload);
	}

	/// <summary>
	///     Import all currently active BIND zones into database zones and disable those BIND providers at runtime.
	/// </summary>
	/// <param name="request">Bulk import settings.</param>
	/// <returns>Batch import/disable result.</returns>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpPost("zones/import-active-bind")]
	[Authorize]
	public async Task<IActionResult?> ImportActiveBindZones([FromBody] ActiveBindImportRequest? request = null)
	{
		var options = request ?? new();
		var result = await dnsService.ImportActiveBindZonesToDatabaseAndDisableAsync(
			                       options.ReplaceExistingRecords,
			                       options.EnableImportedZones
		                       )
		                       .ConfigureAwait(false);

		return Ok(result);
	}
}

#pragma warning restore CS9113