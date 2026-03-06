using Dns.Cli.Middleware;
using Dns.Db.Models.EntityFramework;
using Microsoft.AspNetCore.Http;

namespace Dns.Cli.Extensions;

/// <summary>
///
/// </summary>
public static class HttpContextUserExtensions
{
	/// <summary>
	///
	/// </summary>
	/// <param name="ctx"></param>
	/// <returns></returns>
	public static User? GetCurrentUser(this HttpContext ctx)
		=> ctx.Items.TryGetValue(LoadCurrentUserMiddleware.HttpContextItemKey, out var u)
			? u as User
			: null;
}