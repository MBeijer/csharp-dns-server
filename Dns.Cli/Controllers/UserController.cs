using Dns.Cli.Extensions;
using Dns.Cli.Handlers;
using Dns.Cli.Models.Dto;
using Dns.Db.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Dns.Cli.Controllers;

/// <summary>
///
/// </summary>
/// <param name="userRepository"></param>
/// <param name="jwtTokenHandler"></param>
[ApiController]
[Route("user/")]
public class UserController(
	IUserRepository userRepository,
	IJwtTokenHandler jwtTokenHandler
) : ControllerBase
{
	/// <summary>
	///     Get current user
	/// </summary>
	/// <returns></returns>
	[ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UserDto))]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[Authorize]
	[Produces("application/json")]
	[HttpGet("")]
	public IActionResult? GetUser()
	{
		var user = HttpContext.GetCurrentUser();
		if (user == null) return NotFound();

		return Ok(user.ToDto());
	}

	/*
	/// <summary>
	///     Register new user
	/// </summary>
	/// <returns></returns>
	[ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PlayerAccount))]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[Produces("application/json")]
	[HttpPost("register")]
	public IActionResult? RegisterUser([FromBody] RegisterUser registerUser)
	{
		var status = userRepository.RegisterAccount(registerUser, out var user);
		if (status != RegisterAccountStatus.Success)
			return BadRequest(status.GetDescription("Unknown server error"));

		return Ok(user);
	}
	*/

	/// <summary>
	///     Log in
	/// </summary>
	/// <returns></returns>
	[ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[Produces("application/json")]
	[HttpPost("login")]
	public IActionResult? LoginAsync(LoginRequest loginRequest)
	{
		if (userRepository.VerifyAccount(loginRequest.Account, loginRequest.Password, out var user) && user != null)
		{
			return Ok(jwtTokenHandler.GenerateJwtToken(user));
		}

		return BadRequest();
	}

	/// <summary>
	///     Log in
	/// </summary>
	/// <returns></returns>
	[ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[Produces("application/json")]
	[HttpGet("login")]
	public IActionResult? LoginAsync([FromQuery] string account, [FromQuery] string password)
	{
		if (userRepository.VerifyAccount(account, password, out var user) && user != null)
			return Ok(jwtTokenHandler.GenerateJwtToken(user));

		return BadRequest();
	}
}