using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Dns.Cli.Middleware;

public class PrependBearerSchemeMiddleware(RequestDelegate next)
{
	public async Task Invoke(HttpContext context)
	{
		var authorizationHeader = context.Request.Headers.Authorization.FirstOrDefault();
		if (authorizationHeader != null &&
		    !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			// Prepend "Bearer " to the token
			context.Request.Headers.Authorization = "Bearer " + authorizationHeader;

		await next(context).ConfigureAwait(false);
	}
}