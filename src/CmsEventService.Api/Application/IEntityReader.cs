using CmsEventService.Api.Domain;

namespace CmsEventService.Api.Application;

/// <summary>Read-only access to the stored entities. Returned entities must not be modified.</summary>
public interface IEntityReader
{
    /// <summary>
    /// Returns one page of entities ordered by id; <c>includeHidden</c> adds the unpublished and admin-disabled ones.
    /// </summary>
    Task<PagedResult<CmsEntity>> ListAsync(bool includeHidden, int page, int pageSize, CancellationToken ct);

    /// <summary>Returns an entity, or null when it does not exist or is hidden from the caller.</summary>
    Task<CmsEntity?> FindAsync(string id, bool includeHidden, CancellationToken ct);
}
