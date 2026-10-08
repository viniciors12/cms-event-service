using CmsEventService.Api.Domain;

namespace CmsEventService.Api.Application;

/// <summary>Read-only access to the stored entities. Returned entities must not be modified.</summary>
public interface IEntityReader
{
    /// <param name="includeHidden">
    /// False returns only what regular users may see; true also returns unpublished and admin-disabled entities.
    /// </param>
    Task<PagedResult<CmsEntity>> ListAsync(bool includeHidden, int page, int pageSize, CancellationToken ct);

    Task<CmsEntity?> FindAsync(string id, bool includeHidden, CancellationToken ct);
}
