namespace CmsEventService.Api.Application;

/// <summary>One page of results with the totals needed to navigate.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    /// <summary>Converts each item, keeping the paging information.</summary>
    public PagedResult<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        var mapped = Items
            .Select(selector)
            .ToList();

        return new PagedResult<TResult>(mapped, Page, PageSize, TotalCount);
    }
}
