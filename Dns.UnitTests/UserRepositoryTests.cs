using System.Threading.Tasks;
using Dns.Db.Contexts;
using Dns.Db.Models.EntityFramework;
using Dns.Db.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Dns.UnitTests;

public sealed class UserRepositoryTests : IAsyncLifetime
{
	private SqliteConnection _connection;
	private DnsServerDbContext _context;
	private IPasswordHasher<User> _passwordHasher;
	private UserRepository _target;

	public async Task InitializeAsync()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		await _connection.OpenAsync();
		var options = new DbContextOptionsBuilder<DnsServerDbContext>().UseSqlite(_connection).Options;
		_context = new DnsServerDbContext(options);
		await _context.Database.EnsureCreatedAsync();
		_passwordHasher = Substitute.For<IPasswordHasher<User>>();
		_target = new UserRepository(Substitute.For<ILogger<UserRepository>>(), _context, _passwordHasher);
	}

	public async Task DisposeAsync()
	{
		await _context.DisposeAsync();
		await _connection.DisposeAsync();
	}

	[Fact]
	public async Task VerifyAccount_RehashesWhenNeeded_AndSupportsQueries()
	{
		_context.Users.Add(new User { Account = "admin", Password = "old-hash", Activated = true, AdminLevel = 1 });
		await _context.SaveChangesAsync();

		_passwordHasher.VerifyHashedPassword(Arg.Any<User>(), Arg.Any<string>(), "pw").Returns(PasswordVerificationResult.SuccessRehashNeeded);
		_passwordHasher.HashPassword(Arg.Any<User>(), "pw").Returns("new-hash");

		var verified = _target.VerifyAccount("admin", "pw", out var user);
		Assert.True(verified);
		Assert.NotNull(user);
		Assert.Equal("new-hash", (await _context.Users.FirstAsync()).Password);

		Assert.Single(await _target.GetUsers());
		var fetched = await _target.GetUser("admin");
		fetched.AdminLevel = 2;
		await _target.UpdateUser(fetched);
		Assert.Equal((byte)2, (await _context.Users.FirstAsync()).AdminLevel);
	}

	[Fact]
	public async Task VerifyAccount_RejectsInvalidInputsAndInactiveAccount()
	{
		_context.Users.Add(new User { Account = "inactive", Password = "hash", Activated = false });
		await _context.SaveChangesAsync();

		Assert.False(_target.VerifyAccount(string.Empty, "pw", out _));
		Assert.False(_target.VerifyAccount("inactive", "pw", out _));
		Assert.False(_target.VerifyAccount("missing", "pw", out _));
	}
}