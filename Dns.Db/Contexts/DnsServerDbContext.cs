using Dns.Db.Models.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dns.Db.Contexts;

/// <inheritdoc />
public sealed class DnsServerDbContext : DbContext
{
	/// <inheritdoc />
	public DnsServerDbContext(DbContextOptions<DnsServerDbContext> options) : base(options)
	{
		ChangeTracker.LazyLoadingEnabled = false;
	}

	public DbSet<User>?       Users       { get; set; }
	public DbSet<Zone>?       Zones       { get; set; }
	public DbSet<ZoneRecord>? ZoneRecords { get; set; }


	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		CreateUserModel(modelBuilder.Entity<User>());
		CreateZoneRecordModel(modelBuilder.Entity<ZoneRecord>());
		CreateZoneModel(modelBuilder.Entity<Zone>());
	}

	private static void CreateZoneModel(EntityTypeBuilder<Zone> modelBuilder)
	{
		modelBuilder.HasMany(b => b.Records)
		            .WithOne(b => b.ZoneObj)
		            .HasForeignKey(b => b.Zone)
		            .OnDelete(DeleteBehavior.Cascade);
		modelBuilder.HasIndex(b => b.Suffix).IsUnique();

		modelBuilder.Navigation(b => b.Records).AutoInclude();
	}

	private static void CreateZoneRecordModel(EntityTypeBuilder<ZoneRecord> modelBuilder)
	{
		modelBuilder.HasIndex(b => b.Host);
		modelBuilder.HasIndex(b => b.Zone);

		modelBuilder.HasOne(b => b.ZoneObj)
		            .WithMany(u => u.Records)
		            .HasForeignKey(b => b.Zone)
		            .OnDelete(DeleteBehavior.Restrict);
	}
	
	private static void CreateUserModel(EntityTypeBuilder<User> modelBuilder)
	{
		modelBuilder.HasIndex(u => u.Account).IsUnique();
		modelBuilder.Property(e => e.Account).IsRequired();
	}
}