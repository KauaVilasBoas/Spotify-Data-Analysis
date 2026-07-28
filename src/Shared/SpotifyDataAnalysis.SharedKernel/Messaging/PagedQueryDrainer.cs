using System.Runtime.CompilerServices;

namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>
/// Walks every page of a paginated read query through <see cref="IMediator"/> so a caller can consume an
/// entire result set regardless of page size. Callers that legitimately want the whole set (an alert scan,
/// a module-boundary listing) use this instead of hand-rolling a paging loop.
/// </summary>
/// <remarks>
/// <para>
/// The factory takes a 1-based page number and returns the query for that page. Termination is driven by
/// the first page's <see cref="PagedResult{T}.TotalPages"/> — a single, self-consistent bound derived from
/// <c>COUNT(*) OVER()</c> — so the walk issues exactly <c>ceil(total / pageSize)</c> round-trips and can
/// never loop unbounded, even if a later page returns more or fewer rows than expected.
/// </para>
/// <para>
/// Two shapes are offered over the same walk: <see cref="StreamAsync{TItem}"/> yields items lazily (no
/// intermediate buffer) and <see cref="DrainAsync{TItem}"/> collects them into a flat list.
/// </para>
/// </remarks>
public static class PagedQueryDrainer
{
    /// <summary>
    /// Streams every item across all pages, yielding them lazily as each page arrives. The first page's
    /// <see cref="PagedResult{T}.TotalPages"/> bounds the walk.
    /// </summary>
    public static async IAsyncEnumerable<TItem> StreamAsync<TItem>(
        IMediator mediator,
        Func<int, IRequest<PagedResult<TItem>>> queryForPage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        PagedResult<TItem> firstPage = await mediator.SendAsync(queryForPage(1), cancellationToken);

        foreach (TItem item in firstPage.Items)
            yield return item;

        for (int page = 2; page <= firstPage.TotalPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PagedResult<TItem> next = await mediator.SendAsync(queryForPage(page), cancellationToken);

            foreach (TItem item in next.Items)
                yield return item;
        }
    }

    /// <summary>
    /// Drains every page into a single flat list, bounded by the first page's
    /// <see cref="PagedResult{T}.TotalPages"/>.
    /// </summary>
    public static async Task<IReadOnlyList<TItem>> DrainAsync<TItem>(
        IMediator mediator,
        Func<int, IRequest<PagedResult<TItem>>> queryForPage,
        CancellationToken cancellationToken = default)
    {
        List<TItem> collected = [];

        await foreach (TItem item in StreamAsync(mediator, queryForPage, cancellationToken))
            collected.Add(item);

        return collected;
    }
}
