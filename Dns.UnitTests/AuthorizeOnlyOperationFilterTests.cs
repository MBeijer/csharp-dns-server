using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dns.Cli.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;
using NSubstitute;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace Dns.UnitTests;

public sealed class AuthorizeOnlyOperationFilterTests
{
	private readonly AuthorizeOnlyOperationFilter _target;

	public AuthorizeOnlyOperationFilterTests() => _target = new();

	[Fact]
	public void Apply_AddsSecurityForAuthorizedMethods()
	{
		var secureOperation = new OpenApiOperation();
		var secureContext = CreateOperationFilterContext(typeof(OperationFilterTestEndpoint).GetMethod(nameof(OperationFilterTestEndpoint.Secured))!);
		_target.Apply(secureOperation, secureContext);
		Assert.NotNull(secureOperation.Security);
		Assert.Contains("401", secureOperation.Responses.Keys);
		Assert.Contains("403", secureOperation.Responses.Keys);

		var openOperation = new OpenApiOperation();
		var openContext = CreateOperationFilterContext(typeof(OperationFilterTestEndpoint).GetMethod(nameof(OperationFilterTestEndpoint.Open))!);
		_target.Apply(openOperation, openContext);
		Assert.True(openOperation.Security == null || openOperation.Security.Count == 0);
	}

	private static OperationFilterContext CreateOperationFilterContext(MethodInfo methodInfo)
	{
		var ctor = typeof(OperationFilterContext).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
		var args = ctor.GetParameters().Select(parameter => ResolveParameter(parameter.ParameterType, methodInfo)).ToArray();
		return (OperationFilterContext)ctor.Invoke(args);
	}

	private static object ResolveParameter(Type type, MethodInfo methodInfo)
	{
		if (type == typeof(ApiDescription)) return new ApiDescription();
		if (type == typeof(MethodInfo)) return methodInfo;
		if (type == typeof(SchemaRepository)) return new SchemaRepository();
		if (type == typeof(string)) return "v1";
		if (type.IsInterface) return Substitute.For([type], Array.Empty<object>());
		if (type == typeof(IEnumerable<FilterDescriptor>)) return Array.Empty<FilterDescriptor>();
		if (type == typeof(IList)) return Array.Empty<object>();
		return Activator.CreateInstance(type)!;
	}

	private sealed class OperationFilterTestEndpoint
	{
		[Authorize(Policy = "dns:read")]
		public void Secured()
		{
		}

		public void Open()
		{
		}
	}
}
