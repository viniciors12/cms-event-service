using Microsoft.EntityFrameworkCore;

namespace CmsEventService.Api.Infrastructure;

/// <summary>
/// Used by the read API only. Queries are not tracked, and saving is refused so a read path can never
/// write by accident. Point it at a read-only connection (or a replica) with ConnectionStrings:ReadOnly.
/// </summary>
public sealed class ReadDbContext(DbContextOptions<ReadDbContext> options) : CmsDbContextBase(options)
{
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    private static InvalidOperationException ReadOnly() => new("ReadDbContext is read-only.");
}
