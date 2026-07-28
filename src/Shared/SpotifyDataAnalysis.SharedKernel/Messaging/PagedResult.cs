namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>
/// Paginated result for a <see cref="PagedQuery{TResult}"/>.
/// <see cref="TotalCount"/> comes from <c>COUNT(*) OVER()</c> in the same query (single DB round-trip).
/// </summary>
public sealed class PagedResult<TItem>
{
    public PagedResult(IReadOnlyList<TItem> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<TItem> Items { get; }

    public int TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public static PagedResult<TItem> Empty(int page, int pageSize)
        => new(Array.Empty<TItem>(), totalCount: 0, page, pageSize);
}
