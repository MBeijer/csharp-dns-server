using System.Net;

namespace Dns.Handlers
{
	public class TraefikClientHandler : MyHttpClientHandler
	{
		public TraefikClientHandler() : base(new CookieContainer()) { }
	}
}