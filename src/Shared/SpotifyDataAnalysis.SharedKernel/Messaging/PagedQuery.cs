namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>
/// Base for paginated read-side queries. Provides Page/PageSize and the
/// <see cref="FirstResult"/>/<see cref="LastResult"/> bounds (1-based) used
/// in PostgreSQL pagination via ROW_NUMBER:
/// <c>WHERE row_number BETWEEN @FirstResult AND @LastResult</c>.
/// </summary>
public abstract record PagedQuery<TResult> : IQuery<TResult>
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

    private readonly int _page = 1;
    private readonly int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }

    /// <summary>First row number (1-based) for this page — for <c>ROW_NUMBER BETWEEN</c>.</summary>
    public int FirstResult => ((Page - 1) * PageSize) + 1;

    /// <summary>Last row number (1-based) for this page — for <c>ROW_NUMBER BETWEEN</c>.</summary>
    public int LastResult => Page * PageSize;
}
