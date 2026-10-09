using Microsoft.EntityFrameworkCore;

using CmsEventService.Api.Domain;

namespace CmsEventService.Api.Infrastructure;

/// <summary>The model shared by the writer and the read-only context, so both always agree on the schema.</summary>
public abstract class CmsDbContextBase(DbContextOptions options) : DbContext(options)
{
    /// <summary>The stored entities.</summary>
    public DbSet<CmsEntity> Entities => Set<CmsEntity>();

    /// <summary>Tombstones of deleted entities.</summary>
    public DbSet<DeletedEntity> DeletedEntities => Set<DeletedEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CmsEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(100);
            e.Property(x => x.Payload).IsRequired();

            // Optimistic concurrency: every applied event moves the version or the timestamp forward, so a
            // writer that loaded an older state fails (and the CMS retries) instead of overwriting a newer
            // event. SQLite already serializes writers; this protects a server database with concurrent batches.
            e.Property(x => x.LatestVersion).IsConcurrencyToken();
            e.Property(x => x.LastEventTimestamp).IsConcurrencyToken();
            e.HasIndex(x => new { x.IsPublished, x.IsDisabledByAdmin, x.Id });
        });

        modelBuilder.Entity<DeletedEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(100);
        });
    }
}
