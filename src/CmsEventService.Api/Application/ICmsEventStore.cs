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

    /// <summary>Loads an entity, or returns null when it is not stored.</summary>
    Task<CmsEntity?> FindEntityAsync(string id, CancellationToken ct);

    /// <summary>Loads the tombstone left by a delete, or returns null.</summary>
    Task<DeletedEntity?> FindTombstoneAsync(string id, CancellationToken ct);

    /// <summary>Stages a new entity.</summary>
    void AddEntity(CmsEntity entity);

    /// <summary>Stages the removal of an entity.</summary>
    void RemoveEntity(CmsEntity entity);

    /// <summary>Stages a new tombstone.</summary>
    void AddTombstone(DeletedEntity tombstone);

    /// <summary>Stages the removal of a tombstone.</summary>
    void RemoveTombstone(DeletedEntity tombstone);

    /// <summary>Writes the staged changes.</summary>
    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>Drops every staged change, so a failed event cannot leak into the next one.</summary>
    void DiscardChanges();
}

/// <summary>A unit of work spanning a whole batch. Disposing without committing discards everything.</summary>
public interface IBatchScope : IAsyncDisposable
{
    /// <summary>Commits everything saved during the batch.</summary>
    Task CommitAsync(CancellationToken ct);
}
