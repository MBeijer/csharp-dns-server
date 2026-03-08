using System.Threading.Tasks;
using Dns.Cli.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Dns.UnitTests;

public sealed class PrependBearerSchemeMiddlewareTests
{
	private readonly PrependBearerSchemeMiddleware _target;

	public PrependBearerSchemeMiddlewareTests() => _target = new(_ => Task.CompletedTask);

	[Fact]
	public async Task Invoke_Prepends_WhenMissingScheme()
	{
		var context = new DefaultHttpContext();
		context.Request.Headers.Authorization = "abc";

		await _target.Invoke(context);

		Assert.Equal("Bearer abc", context.Request.Headers.Authorization.ToString());
	}

	[Fact]
	public async Task Invoke_LeavesBearerHeaderUnchanged()
	{
		var context = new DefaultHttpContext();
		context.Request.Headers.Authorization = "Bearer abc";

		await _target.Invoke(context);

		Assert.Equal("Bearer abc", context.Request.Headers.Authorization.ToString());
	}
}
