namespace Dns.Cli.Models.Dto;

/// <summary>
/// API representation of an authenticated user.
/// </summary>
public sealed class UserDto
{
	/// <summary>
	/// User identifier.
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// Account username.
	/// </summary>
	public string? Account { get; set; }

	/// <summary>
	/// Indicates whether the account is activated.
	/// </summary>
	public bool Activated { get; set; }

	/// <summary>
	/// Administrative level for the user.
	/// </summary>
	public byte AdminLevel { get; set; }
}

