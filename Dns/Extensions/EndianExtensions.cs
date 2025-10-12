namespace Dns.Extensions;

public static class EndianExtensions
{
	public static ushort SwapEndian(this ushort val)
		=> (ushort) ((val << 8) | (val >> 8));

	public static uint SwapEndian(this uint val)
		=> (val << 24) | ((val << 8) & 0x00ff0000) | ((val >> 8) & 0x0000ff00) | (val >> 24);
}