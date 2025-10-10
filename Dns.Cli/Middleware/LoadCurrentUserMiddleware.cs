using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Dns.Cli.Middleware;

public sealed class LoadCurrentUserMiddleware(RequestDelegate next)
{
	public const string HttpContextItemKey = "CurrentUser";

	public async Task InvokeAsync(HttpContext context/*, IUserRepository userRepository*/)
	{
		var ct = context.RequestAborted;

		// Get the user id from the auth ticket (JWT, cookie, etc.)
		var username = context.User?.Identity?.Name;

		if (!string.IsNullOrEmpty(username))
		{
			var user = new object()/*await userRepository.GetUser(username, ct).ConfigureAwait(false)*/;

			// Store either the entity or a lightweight DTO
			context.Items[HttpContextItemKey] = user;
		}

		await next(context).ConfigureAwait(false);
	}
}