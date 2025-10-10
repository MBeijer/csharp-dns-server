using System.Collections.Generic;
using Dns.Contracts;
using Microsoft.Extensions.Hosting;

namespace Dns.Services;

public interface IDnsService : IHostedService
{
	public List<IDnsResolver> Resolvers { get; }
}