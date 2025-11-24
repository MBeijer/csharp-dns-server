using Dns.Db.Models.EntityFramework;

namespace Dns.Db.Repositories;

public interface IUserRepository
{
	Task<List<User>>      GetUsers();
	Task<User>            GetUser(string account, CancellationToken token = default);
	Task                  UpdateUser(User user);
}