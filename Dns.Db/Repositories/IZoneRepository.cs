using Dns.Db.Models.EntityFramework;

namespace Dns.Db.Repositories;

public interface IZoneRepository
{
	Task<List<Zone>> GetZones();
	Task<Zone?>      GetZone(string suffix);
	Task             AddZone(Zone zone);
	Task             UpdateZone(Zone zone);
}