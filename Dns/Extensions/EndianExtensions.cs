namespace Dns.Extensions;

public static class EndianExtensions
{
	public static ushort SwapEndian(this ushort val) => (ushort)((val << 8) | (val >> 8));

	public static uint SwapEndian(this uint val) =>
		(val << 24) | ((val << 8) & 0x00ff0000) | ((val >> 8) & 0x0000ff00) | (val >> 24);

	/// <summary>
	///     Converts a ushort from network byte order (big-endian) to host byte order.
	///     Equivalent to SwapEndian but semantically clearer for reading operations.
	/// </summary>
	public static ushort NetworkToHost(this ushort val) => val.SwapEndian();

	/// <summary>
	///     Converts a uint from network byte order (big-endian) to host byte order.
	///     Equivalent to SwapEndian but semantically clearer for reading operations.
	/// </summary>
	public static uint NetworkToHost(this uint val) => val.SwapEndian();

	/// <summary>
	///     Converts a ushort from host byte order to network byte order (big-endian).
	///     Equivalent to SwapEndian but semantically clearer for writing operations.
	/// </summary>
	public static ushort HostToNetwork(this ushort val) => val.SwapEndian();

	/// <summary>
	///     Converts a uint from host byte order to network byte order (big-endian).
	///     Equivalent to SwapEndian but semantically clearer for writing operations.
	/// </summary>
	public static uint HostToNetwork(this uint val) => val.SwapEndian();
}