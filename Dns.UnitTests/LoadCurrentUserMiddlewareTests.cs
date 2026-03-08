using System.Security.Claims;
using System.Threading.Tasks;
using Dns.Cli.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Dns.UnitTests;

public sealed class LoadCurrentUserMiddlewareTests
{
	private bool _called;
	private readonly LoadCurrentUserMiddleware _target;

	public LoadCurrentUserMiddlewareTests()
	{
		_target = new(_ =>
		{
			_called = true;
			return Task.CompletedTask;
		});
	}

	[Fact]
	public async Task InvokeAsync_AddsContextItem_WhenIdentityHasName()
	{
		var context = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "test")),
		};

		await _target.InvokeAsync(context);

		Assert.True(_called);
		Assert.True(context.Items.ContainsKey(LoadCurrentUserMiddleware.HttpContextItemKey));
	}

	[Fact]
	public async Task InvokeAsync_DoesNotAddContextItem_WhenNoName()
	{
		var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

		await _target.InvokeAsync(context);

		Assert.False(context.Items.ContainsKey(LoadCurrentUserMiddleware.HttpContextItemKey));
	}
}
