using Dns.Cli.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Dns.Cli.Extensions;

/// <summary>
///
/// </summary>
public static class MiddlewareExtensions
{
	/// <summary>
	///
	/// </summary>
	/// <param name="app"></param>
	/// <returns></returns>
	public static IApplicationBuilder UseLoadCurrentUser(this IApplicationBuilder app)
		=> app.UseMiddleware<LoadCurrentUserMiddleware>();
}