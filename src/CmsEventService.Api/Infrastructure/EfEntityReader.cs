using CmsEventService.Api.Application;
using CmsEventService.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CmsEventService.Api.Infrastructure;

public sealed class EfEntityReader(AppDbContext db) : IEntityReader
{
    public async Task<PagedResult<CmsEntity>> ListAsync(bool includeHidden, int page, int pageSize, CancellationToken ct)
    {
        var query = Visible(db.Entities.AsNoTracking(), includeHidden);

        var total = await query.CountAsync(ct);

        // Ordered by the primary key so pages are stable between requests.
        var items = await query
            .OrderBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<CmsEntity>(items, page, pageSize, total);
    }

    public Task<CmsEntity?> FindAsync(string id, bool includeHidden, CancellationToken ct) =>
        Visible(db.Entities.AsNoTracking(), includeHidden).FirstOrDefaultAsync(e => e.Id == id, ct);

    private static IQueryable<CmsEntity> Visible(IQueryable<CmsEntity> query, bool includeHidden) =>
        includeHidden ? query : query.Where(CmsEntity.IsVisibleToUsers);
}
