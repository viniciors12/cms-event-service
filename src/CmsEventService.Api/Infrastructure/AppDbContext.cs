using CmsEventService.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CmsEventService.Api.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CmsEntity> Entities => Set<CmsEntity>();

    public DbSet<DeletedEntity> DeletedEntities => Set<DeletedEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CmsEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(100);
            e.Property(x => x.Payload).IsRequired();
        });

        modelBuilder.Entity<DeletedEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(100);
        });
    }
}
