using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dns.Contracts;

namespace Dns;

public interface IDnsServer : IHtmlDump
{
	Task Start(CancellationToken ct);
	void Initialize(List<IDnsResolver> zoneResolvers);
}