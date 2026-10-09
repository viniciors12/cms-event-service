using Microsoft.EntityFrameworkCore.Storage;

using CmsEventService.Api.Application;
using CmsEventService.Api.Domain;

namespace CmsEventService.Api.Infrastructure;

/// <summary>EF Core implementation of <see cref="ICmsEventStore"/>.</summary>
public sealed class EfCmsEventStore(WriteDbContext db) : ICmsEventStore
{
    // Inside this transaction EF Core wraps every SaveChanges in a savepoint and rolls back only that
    // one when it fails, which keeps each event isolated while the batch shares a single commit.
    /// <inheritdoc />
    public async Task<IBatchScope> BeginBatchAsync(CancellationToken ct) =>
        new EfBatchScope(await db.Database.BeginTransactionAsync(ct));

    /// <inheritdoc />
    public async Task<CmsEntity?> FindEntityAsync(string id, CancellationToken ct) =>
        await db.Entities.FindAsync([id], ct);

    /// <inheritdoc />
    public async Task<DeletedEntity?> FindTombstoneAsync(string id, CancellationToken ct) =>
        await db.DeletedEntities.FindAsync([id], ct);

    /// <inheritdoc />
    public void AddEntity(CmsEntity entity) => db.Entities.Add(entity);

    /// <inheritdoc />
    public void RemoveEntity(CmsEntity entity) => db.Entities.Remove(entity);

    /// <inheritdoc />
    public void AddTombstone(DeletedEntity tombstone) => db.DeletedEntities.Add(tombstone);

    /// <inheritdoc />
    public void RemoveTombstone(DeletedEntity tombstone) => db.DeletedEntities.Remove(tombstone);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <inheritdoc />
    public void DiscardChanges() => db.ChangeTracker.Clear();

    private sealed class EfBatchScope(IDbContextTransaction transaction) : IBatchScope
    {
        /// <inheritdoc />
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);

        /// <inheritdoc />
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
