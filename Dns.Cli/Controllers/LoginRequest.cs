using System.Text.Json.Serialization;

namespace Dns.Cli.Controllers;

/// <summary>
///
/// </summary>
public class LoginRequest
{
	/// <summary>
	///
	/// </summary>
	[JsonPropertyName("account")]
	public string Account { get; set; } = "";

	/// <summary>
	///
	/// </summary>
	[JsonPropertyName("password")]
	public string Password { get; set; } = "";
}