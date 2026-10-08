using CmsEventService.Api.Domain;

namespace CmsEventService.Api.Application;

/// <summary>
/// Persistence needed to apply CMS events. Changes are staged by the Add/Remove methods
/// and written together by <see cref="SaveChangesAsync"/>.
/// </summary>
public interface ICmsEventStore
{
    /// <summary>
    /// Groups the saves that follow into one database transaction, so a batch costs a single commit.
    /// A save that fails is undone on its own without aborting the transaction.
    /// </summary>
    Task<IBatchScope> BeginBatchAsync(CancellationToken ct);

    Task<CmsEntity?> FindEntityAsync(string id, CancellationToken ct);

    Task<DeletedEntity?> FindTombstoneAsync(string id, CancellationToken ct);

    void AddEntity(CmsEntity entity);

    void RemoveEntity(CmsEntity entity);

    void AddTombstone(DeletedEntity tombstone);

    void RemoveTombstone(DeletedEntity tombstone);

    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>Drops every staged change, so a failed event cannot leak into the next one.</summary>
    void DiscardChanges();
}

/// <summary>A unit of work spanning a whole batch. Disposing without committing discards everything.</summary>
public interface IBatchScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
