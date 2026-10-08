using CmsEventService.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CmsEventService.Api.Infrastructure;

/// <summary>The model shared by the writer and the read-only context, so both always agree on the schema.</summary>
public abstract class CmsDbContextBase(DbContextOptions options) : DbContext(options)
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
            e.HasIndex(x => new { x.IsPublished, x.IsDisabledByAdmin, x.Id });
        });

        modelBuilder.Entity<DeletedEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(100);
        });
    }
}
