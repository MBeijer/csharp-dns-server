using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Dns.Cli.Controllers;
using Dns.Cli.Extensions;
using Dns.Cli.Handlers;
using Dns.Cli.Middleware;
using Dns.Cli.Models;
using Dns.Db.Models.EntityFramework;
using Dns.Db.Repositories;
using Dns.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Dns.UnitTests;

public sealed class DnsCliCoverageTests
{
#pragma warning disable CS0618
	[Fact]
	public void DumpController_GetDnsResolverData_ReturnsHtmlContent()
	{
		var dnsService = Substitute.For<IDnsService>();
		var dnsServer = Substitute.For<IDnsServer>();
		dnsService.Resolvers.Returns([]);
		var controller = new DumpController(dnsService, dnsServer);

		var result = controller.GetDnsResolverData();

		var content = Assert.IsType<ContentResult>(result);
		Assert.Equal("text/html", content.ContentType);
	}

	[Fact]
	public void DumpController_GetDnsServerData_ReturnsHtmlContent()
	{
		var dnsService = Substitute.For<IDnsService>();
		var dnsServer = Substitute.For<IDnsServer>();
		var controller = new DumpController(dnsService, dnsServer);

		var result = controller.GetDnsServerData();

		var content = Assert.IsType<ContentResult>(result);
		Assert.Equal("text/html", content.ContentType);
	}
#pragma warning restore CS0618

	[Fact]
	public void UserController_GetUser_ReturnsNotFound_WhenNoCurrentUser()
	{
		var controller = CreateUserController(out _, out _);
		controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

		var result = controller.GetUser();

		Assert.IsType<NotFoundResult>(result);
	}

	[Fact]
	public void UserController_GetUser_ReturnsOk_WhenCurrentUserExists()
	{
		var controller = CreateUserController(out _, out _);
		var httpContext = new DefaultHttpContext();
		httpContext.Items[LoadCurrentUserMiddleware.HttpContextItemKey] = new User
		{
			Id = 10,
			Account = "admin",
			Activated = true,
			AdminLevel = 1,
		};
		controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

		var result = controller.GetUser();

		var ok = Assert.IsType<OkObjectResult>(result);
		Assert.NotNull(ok.Value);
	}

	[Fact]
	public void UserController_LoginPost_ReturnsOk_WhenCredentialsValid()
	{
		var controller = CreateUserController(out var userRepository, out var jwtTokenHandler);
		var user = new User { Id = 1, Account = "acc" };
		userRepository.VerifyAccount(Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<User>())
				  .Returns(callInfo =>
				  {
					  callInfo[2] = user;
					  return true;
				  });
		jwtTokenHandler.GenerateJwtToken(user).Returns("token");

		var result = controller.LoginAsync(new LoginRequest { Account = "acc", Password = "pwd" });

		var ok = Assert.IsType<OkObjectResult>(result);
		Assert.Equal("token", ok.Value);
	}

	[Fact]
	public void UserController_LoginGet_ReturnsBadRequest_WhenCredentialsInvalid()
	{
		var controller = CreateUserController(out var userRepository, out _);
		userRepository.VerifyAccount(Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<User>()).Returns(false);

		var result = controller.LoginAsync("bad", "bad");

		Assert.IsType<BadRequestResult>(result);
	}

	[Fact]
	public async Task PrependBearerSchemeMiddleware_Prepends_WhenMissingScheme()
	{
		var middleware = new PrependBearerSchemeMiddleware(_ => Task.CompletedTask);
		var context = new DefaultHttpContext();
		context.Request.Headers.Authorization = "abc";

		await middleware.Invoke(context);

		Assert.Equal("Bearer abc", context.Request.Headers.Authorization.ToString());
	}

	[Fact]
	public async Task PrependBearerSchemeMiddleware_LeavesBearerHeaderUnchanged()
	{
		var middleware = new PrependBearerSchemeMiddleware(_ => Task.CompletedTask);
		var context = new DefaultHttpContext();
		context.Request.Headers.Authorization = "Bearer abc";

		await middleware.Invoke(context);

		Assert.Equal("Bearer abc", context.Request.Headers.Authorization.ToString());
	}

	[Fact]
	public async Task LoadCurrentUserMiddleware_AddsContextItem_WhenIdentityHasName()
	{
		var called = false;
		var middleware = new LoadCurrentUserMiddleware(_ =>
		{
			called = true;
			return Task.CompletedTask;
		});
		var context = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "test")),
		};

		await middleware.InvokeAsync(context);

		Assert.True(called);
		Assert.True(context.Items.ContainsKey(LoadCurrentUserMiddleware.HttpContextItemKey));
	}

	[Fact]
	public async Task LoadCurrentUserMiddleware_DoesNotAddContextItem_WhenNoName()
	{
		var middleware = new LoadCurrentUserMiddleware(_ => Task.CompletedTask);
		var context = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity()),
		};

		await middleware.InvokeAsync(context);

		Assert.False(context.Items.ContainsKey(LoadCurrentUserMiddleware.HttpContextItemKey));
	}

	[Fact]
	public void HttpContextUserExtensions_GetCurrentUser_ReturnsUser_WhenPresent()
	{
		var context = new DefaultHttpContext();
		var user = new User { Account = "alice" };
		context.Items[LoadCurrentUserMiddleware.HttpContextItemKey] = user;

		var current = context.GetCurrentUser();

		Assert.Same(user, current);
	}

	[Fact]
	public void HttpContextUserExtensions_GetCurrentUser_ReturnsNull_WhenMissing()
	{
		var context = new DefaultHttpContext();

		var current = context.GetCurrentUser();

		Assert.Null(current);
	}

	[Fact]
	public void ConfigurationExtensions_ReadJsonElement_HandlesLeafArrayAndObject()
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(
				new Dictionary<string, string>
				{
					["leaf"] = "value",
					["arr:0"] = "a",
					["arr:1"] = "b",
					["obj:name"] = "zone",
				}
			)
			.Build();

		var leaf = configuration.GetSection("leaf").ReadJsonElement();
		var array = configuration.GetSection("arr").ReadJsonElement();
		var obj = configuration.GetSection("obj").ReadJsonElement();

		Assert.Equal("value", leaf.GetString());
		Assert.Equal(2, array.GetArrayLength());
		Assert.Equal("zone", obj.GetProperty("name").GetString());
	}

	[Fact]
	public void ApplicationBuilderExtensions_UpdateDatabase_DoesNotThrow_WhenDbContextNotRegistered()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		var app = new ApplicationBuilder(services);

		var exception = Record.Exception(() => app.UpdateDatabase());

		Assert.Null(exception);
	}

	[Fact]
	public void RequestModels_HaveExpectedDefaults()
	{
		var active = new ActiveBindImportRequest();
		var fileImport = new BindZoneImportRequest();
		var uploadImport = new BindZoneUploadImportRequest();
		var existingUploadImport = new BindZoneExistingUploadImportRequest();

		Assert.True(active.ReplaceExistingRecords);
		Assert.True(active.EnableImportedZones);
		Assert.True(fileImport.Enabled);
		Assert.True(fileImport.ReplaceExistingRecords);
		Assert.Equal(string.Empty, fileImport.FileName);
		Assert.Equal(string.Empty, fileImport.ZoneSuffix);
		Assert.Null(uploadImport.File);
		Assert.True(uploadImport.Enabled);
		Assert.True(uploadImport.ReplaceExistingRecords);
		Assert.Equal(string.Empty, uploadImport.ZoneSuffix);
		Assert.Null(existingUploadImport.File);
		Assert.True(existingUploadImport.ReplaceExistingRecords);
	}

	private static UserController CreateUserController(out IUserRepository userRepository, out IJwtTokenHandler jwtTokenHandler)
	{
		userRepository = Substitute.For<IUserRepository>();
		jwtTokenHandler = Substitute.For<IJwtTokenHandler>();
		return new UserController(userRepository, jwtTokenHandler);
	}
}
