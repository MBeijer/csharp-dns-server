using Dns.Db.Models.EntityFramework;

namespace Dns.Cli.Handlers;

/// <summary>
///
/// </summary>
public interface IJwtTokenHandler
{
	/// <summary>
	///
	/// </summary>
	/// <param name="user"></param>
	/// <returns></returns>
	string GenerateJwtToken(User user);
}