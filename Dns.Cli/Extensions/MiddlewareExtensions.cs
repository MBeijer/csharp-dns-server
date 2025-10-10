using Dns.Cli.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Dns.Cli.Extensions;

public static class MiddlewareExtensions
{
	public static IApplicationBuilder UseLoadCurrentUser(this IApplicationBuilder app)
		=> app.UseMiddleware<LoadCurrentUserMiddleware>();
}