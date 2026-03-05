using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Dns.Cli.Filters;

/// <summary>
///
/// </summary>
public sealed class AuthorizeOnlyOperationFilter : IOperationFilter
{
	/// <summary>
	///
	/// </summary>
	/// <param name="operation"></param>
	/// <param name="context"></param>
	public void Apply(OpenApiOperation operation, OperationFilterContext context)
	{
		// Policy names map to scopes
		var requiredScopes = context.MethodInfo
		                            .GetCustomAttributes(true)
		                            .OfType<AuthorizeAttribute>()
		                            .Select(attribute => attribute.Policy!)
		                            .Distinct()
		                            .ToList();

		if (requiredScopes.Count > 0)
		{
			operation.Responses ??= [];
			operation.Responses.Add("401", new OpenApiResponse { Description = "Unauthorized" });
			operation.Responses.Add("403", new OpenApiResponse { Description = "Forbidden" });

			operation.Security =
			[
				new()
				{
					[new(JwtBearerDefaults.AuthenticationScheme, context.Document)] = requiredScopes,
				},
			];
		}
	}
}