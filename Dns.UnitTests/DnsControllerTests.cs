using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Dns.Contracts;
using Dns.Cli.Controllers;
using Dns.Cli.Models;
using Dns.Db.Models.EntityFramework;
using Dns.Db.Repositories;
using Dns.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Dns.UnitTests;

public sealed class DnsControllerTests
{
	[Fact]
	public void GetDnsResolverData_ReturnsObjectsFromResolvers()
	{
		var resolver = Substitute.For<IDnsResolver>();
		resolver.GetObject().Returns(new { Name = "resolver1" });

		var controller = CreateController(out _, out var dnsService);
		dnsService.Resolvers.Returns([resolver]);

		var result = controller.GetDnsResolverData();
		var ok = Assert.IsType<OkObjectResult>(result);
		Assert.NotNull(ok.Value);
	}

	[Fact]
	public async Task GetZones_ReturnsMappedDtos()
	{
		var controller = CreateController(out var zoneRepository, out _);
		zoneRepository.GetZones().Returns(
			[
				new Zone
				{
					Id = 1,
					Suffix = "example.com",
					Enabled = true,
					Records = new List<ZoneRecord> { new() { Host = "www", Data = "192.0.2.10" } },
				},
			]
		);

		var result = await controller.GetZones();
		var ok = Assert.IsType<OkObjectResult>(result);
		Assert.NotNull(ok.Value);
	}

	[Fact]
	public async Task AddZone_ReturnsCreated_OnSuccess()
	{
		var controller = CreateController(out var zoneRepository, out _);
		zoneRepository.AddZone(Arg.Any<Zone>()).Returns(Task.CompletedTask);

		var result = await controller.AddZone(
						 new Dns.Cli.Models.Dto.ZoneDto
						 {
							 Suffix = "example.com",
							 Enabled = true,
							 Records = [],
						 }
					 );

		Assert.IsType<CreatedResult>(result);
	}

	[Fact]
	public async Task AddZone_ReturnsBadRequest_OnInvalidOperation()
	{
		var controller = CreateController(out var zoneRepository, out _);
		zoneRepository.AddZone(Arg.Any<Zone>())
					  .Returns(_ => throw new InvalidOperationException("broken"));

		var result = await controller.AddZone(new Dns.Cli.Models.Dto.ZoneDto { Suffix = "x", Enabled = true, Records = [] });
		Assert.IsType<BadRequestObjectResult>(result);
	}

	[Fact]
	public async Task UpdateZone_ReturnsOk_OnSuccess()
	{
		var controller = CreateController(out var zoneRepository, out _);
		zoneRepository.UpdateZone(Arg.Any<Zone>()).Returns(Task.CompletedTask);

		var result = await controller.UpdateZone(
						 new Dns.Cli.Models.Dto.ZoneDto
						 {
							 Id = 1,
							 Suffix = "example.com",
							 Enabled = true,
							 Records = [],
						 }
					 );

		Assert.IsType<OkResult>(result);
	}

	[Fact]
	public async Task UpdateZone_ReturnsBadRequest_OnInvalidOperation()
	{
		var controller = CreateController(out var zoneRepository, out _);
		zoneRepository.UpdateZone(Arg.Any<Zone>())
					  .Returns(_ => throw new InvalidOperationException("broken"));

		var result = await controller.UpdateZone(
						 new Dns.Cli.Models.Dto.ZoneDto
						 {
							 Id = 1,
							 Suffix = "example.com",
							 Enabled = true,
							 Records = [],
						 }
					 );

		Assert.IsType<BadRequestObjectResult>(result);
	}

	[Fact]
	public async Task DeleteZone_ReturnsNoContent_WhenDeleted()
	{
		var controller = CreateController(out var zoneRepository, out _);
		zoneRepository.DeleteZone(8).Returns(true);

		var result = await controller.DeleteZone(8);
		Assert.IsType<NoContentResult>(result);
	}

	[Fact]
	public async Task DeleteZone_ReturnsNotFound_WhenMissing()
	{
		var controller = CreateController(out var zoneRepository, out _);
		zoneRepository.DeleteZone(8).Returns(false);

		var result = await controller.DeleteZone(8);
		Assert.IsType<NotFoundResult>(result);
	}

	[Fact]
	public async Task ImportBindZone_ReturnsBadRequest_WhenFileDoesNotExist()
	{
		var controller = CreateController(out _, out _);

		var result = await controller.ImportBindZone(
						 new BindZoneImportRequest
						 {
							 FileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
							 ZoneSuffix = "example.com",
						 }
					 );

		var badRequest = Assert.IsType<BadRequestObjectResult>(result);
		Assert.Contains("Zone file was not found", badRequest.Value?.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ImportBindZone_ReturnsCreated_WhenZoneIsNew()
	{
		var controller = CreateController(out var zoneRepository, out _);
		var zoneFile = WriteTempZoneFile(
			[
				"$TTL 1h",
				"$ORIGIN example.com.",
				"@ IN SOA ns1.example.com. hostmaster.example.com. (",
				"    2024010101",
				"    7200",
				"    3600",
				"    1209600",
				"    3600 )",
				"@ IN NS ns1.example.com.",
				"www IN A 192.0.2.10",
			]
		);

		try
		{
			zoneRepository.GetZone("example.com").Returns((Zone)null);
			zoneRepository.UpsertZone(Arg.Any<Zone>(), Arg.Any<bool>())
						  .Returns(
							  new Zone
							  {
								  Id = 100,
								  Suffix = "example.com",
								  Serial = 2024010101,
								  Enabled = true,
								  Records = new List<ZoneRecord> { new() { Host = "www", Data = "192.0.2.10" } },
							  }
						  );

			var result = await controller.ImportBindZone(
							 new BindZoneImportRequest
							 {
								 FileName = zoneFile,
								 ZoneSuffix = "example.com",
								 Enabled = true,
								 ReplaceExistingRecords = true,
							 }
						 );

			var created = Assert.IsType<CreatedResult>(result);
			Assert.Equal("/dns/zones/100", created.Location);
		}
		finally
		{
			File.Delete(zoneFile);
		}
	}

	[Fact]
	public async Task ImportBindZoneUpload_ReturnsBadRequest_WhenFileMissing()
	{
		var controller = CreateController(out _, out _);

		controller.ModelState.AddModelError("File", "required");
		var result = await controller.ImportBindZoneUpload(
						 new BindZoneUploadImportRequest
						 {
							 File = CreateFormFile("x", "x.zone"),
							 ZoneSuffix = "example.com",
						 }
					 );

		Assert.IsType<ObjectResult>(result);
	}

	[Fact]
	public async Task ImportBindZoneUpload_ReturnsBadRequest_WhenFileIsInvalidBind()
	{
		var controller = CreateController(out _, out _);
		var file = CreateFormFile("not-a-bind-zone", "broken.zone");

		var result = await controller.ImportBindZoneUpload(
						 new BindZoneUploadImportRequest
						 {
							 File = file,
							 ZoneSuffix = "example.com",
						 }
					 );

		var badRequest = Assert.IsType<BadRequestObjectResult>(result);
		Assert.Contains("Unable to parse BIND zone file", badRequest.Value?.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ImportBindZoneIntoExistingZone_ReturnsNotFound_WhenZoneIdMissing()
	{
		var controller = CreateController(out var zoneRepository, out _);
		zoneRepository.GetZone(42).Returns((Zone)null);

		var result = await controller.ImportBindZoneIntoExistingZone(
						 42,
						 new BindZoneExistingUploadImportRequest
						 {
							 File = CreateFormFile("x", "x.zone"),
						 }
					 );

		Assert.IsType<NotFoundObjectResult>(result);
	}

	[Fact]
	public async Task ImportBindZoneIntoExistingZone_ReturnsBadRequest_WhenSuffixMissing()
	{
		var controller = CreateController(out var zoneRepository, out _);
		zoneRepository.GetZone(7).Returns(new Zone { Id = 7, Suffix = " " });

		var result = await controller.ImportBindZoneIntoExistingZone(
						 7,
						 new BindZoneExistingUploadImportRequest
						 {
							 File = CreateFormFile("x", "x.zone"),
						 }
					 );

		Assert.IsType<BadRequestObjectResult>(result);
	}

	[Fact]
	public async Task ImportBindZoneIntoExistingZone_UsesExistingZoneSettings_AndCanAddWithoutReplace()
	{
		var controller = CreateController(out var zoneRepository, out _);
		var file = CreateFormFile(
			string.Join(
				Environment.NewLine,
				[
					"$TTL 1h",
					"$ORIGIN existing.com.",
					"@ IN SOA ns1.existing.com. hostmaster.existing.com. (",
					"    2024010101",
					"    7200",
					"    3600",
					"    1209600",
					"    3600 )",
					"@ IN NS ns1.existing.com.",
					"www IN A 192.0.2.44",
				]
			),
			"existing.zone"
		);

		var existingZone = new Zone { Id = 8, Suffix = "existing.com", Enabled = false };
		zoneRepository.GetZone(8).Returns(existingZone);
		zoneRepository.GetZone("existing.com").Returns(existingZone);
		zoneRepository.UpsertZone(Arg.Any<Zone>(), false)
					  .Returns(
						  new Zone
						  {
							  Id = 8,
							  Suffix = "existing.com",
							  Enabled = false,
							  Serial = 2024010102,
							  Records = new List<ZoneRecord> { new() { Host = "www", Data = "192.0.2.44" } },
						  }
					  );

		var result = await controller.ImportBindZoneIntoExistingZone(
						 8,
						 new BindZoneExistingUploadImportRequest
						 {
							 File = file,
							 ReplaceExistingRecords = false,
						 }
					 );

		Assert.IsType<OkObjectResult>(result);
		await zoneRepository.Received(1).UpsertZone(Arg.Any<Zone>(), false);
	}

	[Fact]
	public async Task ImportBindZoneIntoExistingZone_ReturnsBadRequest_WhenUploadMissing()
	{
		var controller = CreateController(out _, out _);

		var result = await controller.ImportBindZoneIntoExistingZone(
						 8,
						 new BindZoneExistingUploadImportRequest
						 {
							 File = null,
							 ReplaceExistingRecords = true,
						 }
					 );

		Assert.IsType<BadRequestObjectResult>(result);
	}

	[Fact]
	public async Task ImportBindZoneIntoExistingZone_ReturnsValidationProblem_WhenModelInvalid()
	{
		var controller = CreateController(out _, out _);
		controller.ModelState.AddModelError("File", "required");

		var result = await controller.ImportBindZoneIntoExistingZone(
						 8,
						 new BindZoneExistingUploadImportRequest
						 {
							 File = CreateFormFile("x", "x.zone"),
							 ReplaceExistingRecords = true,
						 }
					 );

		Assert.IsType<ObjectResult>(result);
	}

	[Fact]
	public async Task ImportBindZoneIntoExistingZone_ReturnsBadRequest_WhenParseFails()
	{
		var controller = CreateController(out var zoneRepository, out _);
		zoneRepository.GetZone(8).Returns(new Zone { Id = 8, Suffix = "existing.com", Enabled = true });

		var result = await controller.ImportBindZoneIntoExistingZone(
						 8,
						 new BindZoneExistingUploadImportRequest
						 {
							 File = CreateFormFile("broken", "broken.zone"),
							 ReplaceExistingRecords = true,
						 }
					 );

		Assert.IsType<BadRequestObjectResult>(result);
	}

	[Fact]
	public async Task ImportActiveBindZones_UsesDefaults_WhenRequestIsNull()
	{
		var controller = CreateController(out _, out var dnsService);
		dnsService.ImportActiveBindZonesToDatabaseAndDisableAsync(true, true)
				  .Returns(new BindZoneImportBatchResult());

		var result = await controller.ImportActiveBindZones(null);

		Assert.IsType<OkObjectResult>(result);
		await dnsService.Received(1).ImportActiveBindZonesToDatabaseAndDisableAsync(true, true);
	}

	[Fact]
	public async Task ImportActiveBindZones_UsesProvidedOptions()
	{
		var controller = CreateController(out _, out var dnsService);
		dnsService.ImportActiveBindZonesToDatabaseAndDisableAsync(false, false)
				  .Returns(new BindZoneImportBatchResult());

		var result = await controller.ImportActiveBindZones(
						 new ActiveBindImportRequest
						 {
							 ReplaceExistingRecords = false,
							 EnableImportedZones = false,
						 }
					 );

		Assert.IsType<OkObjectResult>(result);
		await dnsService.Received(1).ImportActiveBindZonesToDatabaseAndDisableAsync(false, false);
	}

	[Fact]
	public void BindZoneUploadImportRequest_HasExpectedDefaults()
	{
		var request = new BindZoneUploadImportRequest();
		Assert.Null(request.File);
		Assert.Equal(string.Empty, request.ZoneSuffix);
		Assert.True(request.Enabled);
		Assert.True(request.ReplaceExistingRecords);
	}

	[Fact]
	public void BindZoneExistingUploadImportRequest_HasExpectedDefaults()
	{
		var request = new BindZoneExistingUploadImportRequest();
		Assert.Null(request.File);
		Assert.True(request.ReplaceExistingRecords);
	}

	private static DnsController CreateController(out IZoneRepository zoneRepository, out IDnsService dnsService)
	{
		dnsService = Substitute.For<IDnsService>();
		dnsService.Resolvers.Returns([]);

		var dnsServer = Substitute.For<IDnsServer>();
		zoneRepository = Substitute.For<IZoneRepository>();
		return new DnsController(dnsService, dnsServer, zoneRepository);
	}

	private static IFormFile CreateFormFile(string content, string fileName)
	{
		var bytes = Encoding.UTF8.GetBytes(content);
		var stream = new MemoryStream(bytes);
		return new FormFile(stream, 0, bytes.Length, "file", fileName)
		{
			Headers = new HeaderDictionary(),
			ContentType = "text/plain",
		};
	}

	private static string WriteTempZoneFile(IEnumerable<string> lines)
	{
		var path = Path.Combine(Path.GetTempPath(), $"bind-{Guid.NewGuid():N}.zone");
		File.WriteAllLines(path, lines);
		return path;
	}
}
