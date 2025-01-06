using System.Diagnostics;

namespace Dns.Services;

public static class DebuggingService
{
	private static bool _debugging;

	public static bool RunningInDebugMode()
	{
		WellAreWe();
		return _debugging;
	}

	[Conditional("DEBUG")]
	private static void WellAreWe() => _debugging = true;
}