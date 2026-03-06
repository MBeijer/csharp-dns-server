using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Dns.Config;
using Dns.Db.Models.EntityFramework;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Dns.Cli.Handlers;

/// <summary>
///
/// </summary>
public class JwtTokenHandler : IJwtTokenHandler
{
	private readonly JwtSecurityTokenHandler _tokenHandler;
	private readonly SigningCredentials _signingCredentials;

	/// <summary>
	///
	/// </summary>
	/// <param name="serverOptions"></param>
	public JwtTokenHandler(IOptions<ServerOptions> serverOptions)
	{
		_tokenHandler = new();
		var key = Encoding.ASCII.GetBytes(serverOptions.Value.WebServer.JwtSecretKey);
		_signingCredentials = new(
			new SymmetricSecurityKey(key),
			SecurityAlgorithms.HmacSha256Signature
		);
	}

	/// <summary>
	///
	/// </summary>
	/// <param name="user"></param>
	/// <returns></returns>
	public string GenerateJwtToken(User user)
	{
		var tokenDescriptor = new SecurityTokenDescriptor
		{
			Subject = new(
				[
					new(ClaimTypes.NameIdentifier, user.Id.ToString()),
					new(ClaimTypes.Name, user.Account!),
					new(ClaimTypes.Upn, user.Account!),
				]
			),
			Expires = DateTime.UtcNow.AddDays(7),
			SigningCredentials = _signingCredentials,
		};
		var token = _tokenHandler.CreateToken(tokenDescriptor);
		return _tokenHandler.WriteToken(token);
	}
}