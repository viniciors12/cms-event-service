using CmsEventService.Api.Domain;

namespace CmsEventService.Api.Application;

/// <summary>
/// Persistence needed to apply CMS events. Changes are staged by the Add/Remove methods
/// and written together by <see cref="SaveChangesAsync"/>.
/// </summary>
public interface ICmsEventStore
{
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
