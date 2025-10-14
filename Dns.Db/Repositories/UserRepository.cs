using System.Globalization;
using Dns.Db.Contexts;
using Dns.Db.Models.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dns.Db.Repositories;

#pragma warning disable CS9113

public class UserRepository(ILogger<UserRepository> logger, DnsServerDbContext dbContext) : IUserRepository
{
	public Task<List<User>> GetUsers() => dbContext.Users!.AsNoTracking().ToListAsync();

	public Task<User> GetUser(string account, CancellationToken token = default) =>
		dbContext.Users!.FirstAsync(u => EF.Functions.Like(u.Account!, account), cancellationToken: token);

	public async Task UpdateUser(User user)
	{
		dbContext.Users!.Update(user);
		await dbContext.SaveChangesAsync().ConfigureAwait(false);
	}
}

#pragma warning restore CS9113