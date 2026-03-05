using Dns.Db.Contexts;
using Dns.Db.Models.EntityFramework;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dns.Db.Repositories;

#pragma warning disable CS9113

public class UserRepository(ILogger<UserRepository> logger, DnsServerDbContext dbContext, IPasswordHasher<User> userPasswordHasher) : IUserRepository
{

	public bool VerifyAccount(string username, string password, out User? user)
	{
		user = null;

		if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
		{
			return false;
		}

#pragma warning disable CA1862
		var accountResult = dbContext.Users.FirstOrDefault(u => u.Account!.ToLower() == username.ToLower());
#pragma warning restore CA1862

		if (accountResult == null) return false;

		if (!accountResult.Activated) return false;

		var verify = userPasswordHasher.VerifyHashedPassword(accountResult, accountResult.Password??"", password);

		if (verify == PasswordVerificationResult.Success)
		{
			user = accountResult;
			return true;
		}

		if (verify == PasswordVerificationResult.SuccessRehashNeeded)
		{
			accountResult.Password = userPasswordHasher.HashPassword(accountResult, password);

			dbContext.Users.Update(accountResult);
			dbContext.SaveChanges();
		}

		user = accountResult;
		return true;
	}

	/*
	public RegisterAccountStatus RegisterAccount(RegisterUser registerAccount, out User? user)
	{
		user = null;

		if (string.IsNullOrEmpty(registerAccount.Username)) return RegisterAccountStatus.InvalidUsername;

		var account = dbContext.Users.FirstOrDefault(u => EF.Functions.Like(u.Username!, registerAccount.Username));

		if (account != null) return RegisterAccountStatus.UsernameExists;

		if (string.IsNullOrEmpty(registerAccount.Password)) return RegisterAccountStatus.InvalidPassword;

		if (registerAccount.Password != registerAccount.PasswordRepeated) return RegisterAccountStatus.PasswordMismatch;

		var createUser = new User
		{
			Username   = registerAccount.Username,
			Email      = registerAccount.Email,
			Password   = "",
			Activated  = true,
			AdminLevel = 0,
			Banned     = false,
		};

		createUser.Password = userPasswordHasher.HashPassword(createUser, registerAccount.Password);

		dbContext.Users.Add(createUser);
		dbContext.SaveChanges();

		return RegisterAccountStatus.Success;
	}
	*/

	public Task<List<User>> GetUsers() => dbContext.Users!.AsNoTracking().ToListAsync();

	public Task<User> GetUser(string account, CancellationToken token = default) =>
		dbContext.Users!.FirstAsync(u => EF.Functions.Like(u.Account!, account), token);

	public async Task UpdateUser(User user)
	{
		dbContext.Users!.Update(user);
		await dbContext.SaveChangesAsync().ConfigureAwait(false);
	}
}

#pragma warning restore CS9113