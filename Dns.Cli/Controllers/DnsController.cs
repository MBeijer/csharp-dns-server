using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dns.Cli.Models;
using Dns.Cli.Models.Dto;
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
	public async Task<IActionResult?> GetZones()
	{
		var zones = await zoneRepository.GetZones().ConfigureAwait(false);
		return Ok(zones.Select(z => z.ToDto()).ToList());
	}

	/// <summary>
	///     Get database zones
	/// </summary>
	/// <returns>html</returns>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpPut("zones")]
	[Authorize]
	public async Task<IActionResult?> AddZone([FromBody] ZoneDto zoneDto)
	{
		try
		{
			var zone = zoneDto.ToEntity();
			await zoneRepository.AddZone(zone).ConfigureAwait(false);
			return Created();
		}
		catch (InvalidOperationException ex)
		{
			return BadRequest(ex.Message);
		}
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
	public async Task<IActionResult?> UpdateZone([FromBody] ZoneDto zoneDto)
	{
		try
		{
			var zone = zoneDto.ToEntity();
			await zoneRepository.UpdateZone(zone).ConfigureAwait(false);
			return Ok();
		}
		catch (InvalidOperationException ex)
		{
			return BadRequest(ex.Message);
		}
	}

	/// <summary>
	///     Delete database zone
	/// </summary>
	/// <param name="id">Zone identifier.</param>
	/// <returns>HTTP status code.</returns>
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[HttpDelete("zones/{id:int}")]
	[Authorize]
	public async Task<IActionResult?> DeleteZone([FromRoute] int id)
	{
		var deleted = await zoneRepository.DeleteZone(id).ConfigureAwait(false);
		if (!deleted) return NotFound();

		return NoContent();
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

		return await UpsertImportedZoneAsync(parsedZone, request.ZoneSuffix, request.Enabled, request.ReplaceExistingRecords)
			.ConfigureAwait(false);
	}

	/// <summary>
	///     Import a BIND zone file upload into the database-backed zone model.
	/// </summary>
	/// <param name="request">Multipart form import settings.</param>
	/// <returns>Imported zone summary.</returns>
	[ProducesResponseType(StatusCodes.Status201Created)]
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpPost("zones/import-bind-upload")]
	[Authorize]
	public async Task<IActionResult?> ImportBindZoneUpload([FromForm] BindZoneUploadImportRequest request)
	{
		if (!ModelState.IsValid) return ValidationProblem(ModelState);
		if (request.File == null || request.File.Length <= 0) return BadRequest("A non-empty BIND zone file is required.");

		var parseResult = await ParseUploadedZoneAsync(request.File, request.ZoneSuffix).ConfigureAwait(false);
		if (parseResult.Error != null) return parseResult.Error;

		return await UpsertImportedZoneAsync(
				   parseResult.ParsedZone!,
				   request.ZoneSuffix,
				   request.Enabled,
				   request.ReplaceExistingRecords
			   )
			   .ConfigureAwait(false);
	}

	/// <summary>
	///     Import a BIND zone file upload into an existing database zone.
	/// </summary>
	/// <param name="id">Target zone id.</param>
	/// <param name="request">Multipart form import settings.</param>
	/// <returns>Updated zone summary.</returns>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[HttpPost("zones/{id:int}/import-bind-upload")]
	[Authorize]
	public async Task<IActionResult?> ImportBindZoneIntoExistingZone(
		[FromRoute] int id,
		[FromForm] BindZoneExistingUploadImportRequest request
	)
	{
		if (!ModelState.IsValid) return ValidationProblem(ModelState);
		if (request.File == null || request.File.Length <= 0) return BadRequest("A non-empty BIND zone file is required.");

		var existingZone = await zoneRepository.GetZone(id).ConfigureAwait(false);
		if (existingZone == null) return NotFound($"Zone '{id}' was not found.");

		var zoneSuffix = existingZone.Suffix;
		if (string.IsNullOrWhiteSpace(zoneSuffix))
			return BadRequest($"Zone '{id}' has no suffix and cannot be imported.");

		var parseResult = await ParseUploadedZoneAsync(request.File, zoneSuffix).ConfigureAwait(false);
		if (parseResult.Error != null) return parseResult.Error;

		return await UpsertImportedZoneAsync(
				   parseResult.ParsedZone!,
				   zoneSuffix,
				   existingZone.Enabled,
				   request.ReplaceExistingRecords
			   )
			   .ConfigureAwait(false);
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

	private async Task<IActionResult> UpsertImportedZoneAsync(
		Dns.Models.Zone parsedZone,
		string zoneSuffix,
		bool enabled,
		bool replaceExistingRecords
	)
	{
		var dbZone = BindZoneImportMapper.ToDbZone(parsedZone, zoneSuffix, enabled);
		var existing = await zoneRepository.GetZone(dbZone.Suffix!).ConfigureAwait(false);
		var upserted = await zoneRepository.UpsertZone(dbZone, replaceExistingRecords).ConfigureAwait(false);

		var payload = new
		{
			id = upserted.Id,
			suffix = upserted.Suffix,
			serial = upserted.Serial,
			enabled = upserted.Enabled,
			recordCount = upserted.Records?.Count ?? dbZone.Records?.Count ?? 0,
		};

		return existing == null ? Created($"/dns/zones/{upserted.Id}", payload) : Ok(payload);
	}

	private static async Task<(Dns.Models.Zone? ParsedZone, IActionResult? Error)> ParseUploadedZoneAsync(
		IFormFile file,
		string zoneSuffix
	)
	{
		var tempFilePath = Path.Combine(Path.GetTempPath(), $"bind-import-{Guid.NewGuid():N}.zone");
		try
		{
			using (var target = System.IO.File.Create(tempFilePath))
			{
				await file.CopyToAsync(target).ConfigureAwait(false);
			}

			try
			{
				var parsedZone = BindZoneProvider.ParseZoneFile(tempFilePath, zoneSuffix);
				return (parsedZone, null);
			}
			catch (Exception ex)
			{
				return (null, new BadRequestObjectResult($"Unable to parse BIND zone file: {ex.Message}"));
			}
		}
		finally
		{
			if (System.IO.File.Exists(tempFilePath))
				System.IO.File.Delete(tempFilePath);
		}
	}
}

#pragma warning restore CS9113