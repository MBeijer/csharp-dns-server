using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Dns.Cli.Middleware;

/// <summary>
/// </summary>
/// <param name="next"></param>
public sealed class LoadCurrentUserMiddleware(RequestDelegate next)
{
	/// <summary>
	/// </summary>
	public const string HttpContextItemKey = "CurrentUser";

	/// <summary>
	/// </summary>
	/// <param name="context"></param>
	public async Task InvokeAsync(HttpContext context /*, IUserRepository userRepository*/)
	{
		var ct = context.RequestAborted;

		var username = context.User?.Identity?.Name;

		if (!string.IsNullOrEmpty(username))
		{
			var user = new object() /*await userRepository.GetUser(username, ct).ConfigureAwait(false)*/;

			context.Items[HttpContextItemKey] = user;
		}

		await next(context).ConfigureAwait(false);
	}
}
