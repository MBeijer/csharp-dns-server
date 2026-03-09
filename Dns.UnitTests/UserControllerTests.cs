using Dns.Cli.Controllers;
using Dns.Cli.Handlers;
using Dns.Cli.Middleware;
using Dns.Db.Models.EntityFramework;
using Dns.Db.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Dns.UnitTests;

public sealed class UserControllerTests
{
	private readonly IUserRepository _userRepository;
	private readonly IJwtTokenHandler _jwtTokenHandler;
	private readonly UserController _target;

	public UserControllerTests()
	{
		_userRepository = Substitute.For<IUserRepository>();
		_jwtTokenHandler = Substitute.For<IJwtTokenHandler>();
		_target = new UserController(_userRepository, _jwtTokenHandler)
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
		};
	}

	[Fact]
	public void GetUser_ReturnsNotFound_WhenNoCurrentUser()
	{
		var result = _target.GetUser();
		Assert.IsType<NotFoundResult>(result);
	}

	[Fact]
	public void GetUser_ReturnsOk_WhenCurrentUserExists()
	{
		_target.HttpContext.Items[LoadCurrentUserMiddleware.HttpContextItemKey] = new User
		{
			Id = 10,
			Account = "admin",
			Activated = true,
			AdminLevel = 1,
		};

		var result = _target.GetUser();
		var ok = Assert.IsType<OkObjectResult>(result);
		Assert.NotNull(ok.Value);
	}

	[Fact]
	public void LoginPost_ReturnsOk_WhenCredentialsValid()
	{
		var user = new User { Id = 1, Account = "acc" };
		_userRepository.VerifyAccount(Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<User>())
			.Returns(callInfo =>
			{
				callInfo[2] = user;
				return true;
			});
		_jwtTokenHandler.GenerateJwtToken(user).Returns("token");

		var result = _target.LoginAsync(new LoginRequest { Account = "acc", Password = "pwd" });
		var ok = Assert.IsType<OkObjectResult>(result);
		Assert.Equal("token", ok.Value);
	}

	[Fact]
	public void LoginGet_ReturnsBadRequest_WhenCredentialsInvalid()
	{
		_userRepository.VerifyAccount(Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<User>()).Returns(false);

		var result = _target.LoginAsync("bad", "bad");
		Assert.IsType<BadRequestResult>(result);
	}
}