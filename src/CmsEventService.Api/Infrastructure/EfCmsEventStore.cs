using CmsEventService.Api.Application;
using CmsEventService.Api.Domain;

namespace CmsEventService.Api.Infrastructure;

public sealed class EfCmsEventStore(AppDbContext db) : ICmsEventStore
{
    public async Task<CmsEntity?> FindEntityAsync(string id, CancellationToken ct) =>
        await db.Entities.FindAsync([id], ct);

    public async Task<DeletedEntity?> FindTombstoneAsync(string id, CancellationToken ct) =>
        await db.DeletedEntities.FindAsync([id], ct);

    public void AddEntity(CmsEntity entity) => db.Entities.Add(entity);

    public void RemoveEntity(CmsEntity entity) => db.Entities.Remove(entity);

    public void AddTombstone(DeletedEntity tombstone) => db.DeletedEntities.Add(tombstone);

    public void RemoveTombstone(DeletedEntity tombstone) => db.DeletedEntities.Remove(tombstone);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public void DiscardChanges() => db.ChangeTracker.Clear();
}
