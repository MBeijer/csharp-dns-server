using System.Net;
using System.Net.Http;

namespace Dns.Handlers
{
	public class MyHttpClientHandler : HttpClientHandler
	{
		protected MyHttpClientHandler(CookieContainer cookieContainer)
		{
			ClientCertificateOptions = ClientCertificateOption.Manual;
			ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true;
			AllowAutoRedirect = false;
			CookieContainer = cookieContainer;
		}
	}
}