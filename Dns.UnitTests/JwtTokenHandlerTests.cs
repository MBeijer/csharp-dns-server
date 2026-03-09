using System.IdentityModel.Tokens.Jwt;
using Dns.Cli.Handlers;
using Dns.Config;
using Dns.Db.Models.EntityFramework;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dns.UnitTests;

public sealed class JwtTokenHandlerTests
{
	private readonly JwtTokenHandler _target;

	public JwtTokenHandlerTests() =>
		_target = new(
			Options.Create(new ServerOptions { WebServer = new WebServerOptions { JwtSecretKey = "this-is-a-long-enough-secret-key" } })
		);

	[Fact]
	public void GenerateJwtToken_ContainsExpectedClaims()
	{
		var token = _target.GenerateJwtToken(new User { Id = 7, Account = "admin" });
		var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
		Assert.Contains(jwt.Claims, c => c.Type == "nameid" && c.Value == "7");
		Assert.Contains(jwt.Claims, c => c.Type == "unique_name" && c.Value == "admin");
	}
}