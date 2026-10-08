using CmsEventService.Api.Application;
using CmsEventService.Api.Domain;
using Microsoft.EntityFrameworkCore.Storage;

namespace CmsEventService.Api.Infrastructure;

public sealed class EfCmsEventStore(WriteDbContext db) : ICmsEventStore
{
    // Inside this transaction EF Core wraps every SaveChanges in a savepoint and rolls back only that
    // one when it fails, which keeps each event isolated while the batch shares a single commit.
    public async Task<IBatchScope> BeginBatchAsync(CancellationToken ct) =>
        new EfBatchScope(await db.Database.BeginTransactionAsync(ct));

    public async Task<CmsEntity?> FindEntityAsync(string id, CancellationToken ct) =>
        await db.Entities.FindAsync([id], ct);

    public async Task<DeletedEntity?> FindTombstoneAsync(string id, CancellationToken ct) =>
        await db.DeletedEntities.FindAsync([id], ct);

    public void AddEntity(CmsEntity entity) => db.Entities.Add(entity);

    public void RemoveEntity(CmsEntity entity) => db.Entities.Remove(entity);

    public void AddTombstone(DeletedEntity tombstone) => db.DeletedEntities.Add(tombstone);

    public void RemoveTombstone(DeletedEntity tombstone) => db.DeletedEntities.Remove(tombstone);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public void DiscardChanges() => db.ChangeTracker.Clear();

    private sealed class EfBatchScope(IDbContextTransaction transaction) : IBatchScope
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
