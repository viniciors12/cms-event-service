using Microsoft.EntityFrameworkCore;

using CmsEventService.Api.Application;
using CmsEventService.Api.Domain;

namespace CmsEventService.Api.Infrastructure;

/// <summary>Reads through <see cref="ReadDbContext"/>, which never tracks entities.</summary>
public sealed class EfEntityReader(ReadDbContext db) : IEntityReader
{
    /// <inheritdoc />
    public async Task<PagedResult<CmsEntity>> ListAsync(bool includeHidden, int page, int pageSize, CancellationToken ct)
    {
        var query = Visible(db.Entities, includeHidden);

        var total = await query.CountAsync(ct);

        // Ordered by id so pages are stable between requests; both orderings are served by an index.
        var items = await query
            .OrderBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<CmsEntity>(items, page, pageSize, total);
    }

    /// <inheritdoc />
    public Task<CmsEntity?> FindAsync(string id, bool includeHidden, CancellationToken ct) =>
        Visible(db.Entities, includeHidden).FirstOrDefaultAsync(e => e.Id == id, ct);

    private static IQueryable<CmsEntity> Visible(IQueryable<CmsEntity> query, bool includeHidden) =>
        includeHidden ? query : query.Where(CmsEntity.IsVisibleToUsers);
}
