using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

/// <summary>
/// Recorte de popularidade por gênero: média, mediana e contagem de faixas por gênero, paginado.
/// </summary>
/// <remarks>
/// Faixas sem gênero não desaparecem: caem num bucket explícito <c>(sem gênero)</c>, de modo que a soma das
/// contagens feche com o total do catálogo. A agregação é sobre <c>popularity</c>, coluna própria, então a
/// imputação de audio-features não a distorce — ainda assim cada linha reporta
/// <c>ImputedTrackCount</c>, porque o gênero em si vem do jsonb e faixas imputadas entram no bucket.
/// </remarks>
public sealed record GetGenreInsightsQuery : PagedQuery<PagedResult<GenreInsightItem>>
{
    /// <summary>Critério de ordenação.</summary>
    public GenreInsightSort Sort { get; init; } = GenreInsightSort.AveragePopularityDesc;
}

/// <summary>Ordenações suportadas pelo recorte por gênero.</summary>
public enum GenreInsightSort
{
    AveragePopularityDesc,
    TrackCountDesc,
    GenreAsc
}

/// <summary>
/// Um gênero no recorte. <paramref name="Genre"/> é <c>(sem gênero)</c> para as faixas cujo jsonb não declara
/// gênero.
/// </summary>
public sealed record GenreInsightItem(
    string Genre,
    double AveragePopularity,
    double MedianPopularity,
    long TrackCount,
    long ImputedTrackCount);

internal sealed class GetGenreInsightsQueryHandler
    : BaseDataAccess, IQueryHandler<GetGenreInsightsQuery, PagedResult<GenreInsightItem>>
{
    /// <summary>Rótulo do bucket das faixas sem gênero declarado.</summary>
    internal const string NoGenreLabel = "(sem gênero)";

    internal const string Sql =
        """
        WITH grouped AS (
            SELECT
                COALESCE(t.audio_features ->> 'Genre', @NoGenreLabel)                       AS genre,
                AVG(t.popularity)::double precision                                         AS average_popularity,
                percentile_cont(0.5) WITHIN GROUP (ORDER BY t.popularity)::double precision AS median_popularity,
                COUNT(*)                                                                    AS track_count,
                COUNT(*) FILTER (
                    WHERE COALESCE((t.audio_features ->> 'IsImputed')::boolean, false)
                )                                                                           AS imputed_track_count
            FROM catalog.tracks AS t
            GROUP BY 1
        ),
        records AS (
            SELECT
                genre,
                average_popularity,
                median_popularity,
                track_count,
                imputed_track_count,
                ROW_NUMBER() OVER (
                    ORDER BY
                        CASE WHEN @SortBy = 'average' THEN average_popularity END DESC,
                        CASE WHEN @SortBy = 'count' THEN track_count END DESC,
                        CASE WHEN @SortBy = 'genre' THEN genre END ASC,
                        genre ASC
                )        AS row_number,
                COUNT(*) OVER () AS total_count
            FROM grouped
        )
        SELECT
            genre               AS "Genre",
            average_popularity  AS "AveragePopularity",
            median_popularity   AS "MedianPopularity",
            track_count         AS "TrackCount",
            imputed_track_count AS "ImputedTrackCount",
            total_count         AS "TotalCount"
        FROM records
        WHERE row_number BETWEEN @FirstResult AND @LastResult
        ORDER BY row_number;
        """;

    public GetGenreInsightsQueryHandler(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<PagedResult<GenreInsightItem>> HandleAsync(
        GetGenreInsightsQuery request, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        var command = new CommandDefinition(
            Sql,
            new
            {
                NoGenreLabel,
                SortBy = SortKeyOf(request.Sort),
                request.FirstResult,
                request.LastResult
            },
            cancellationToken: cancellationToken);

        IReadOnlyList<GenreRow> rows = (await connection.QueryAsync<GenreRow>(command)).AsList();

        int totalCount = rows.Count > 0 ? (int)rows[0].TotalCount : 0;

        IReadOnlyList<GenreInsightItem> items = rows
            .Select(row => new GenreInsightItem(
                row.Genre, row.AveragePopularity, row.MedianPopularity,
                row.TrackCount, row.ImputedTrackCount))
            .ToArray();

        return new PagedResult<GenreInsightItem>(items, totalCount, request.Page, request.PageSize);
    }

    internal static string SortKeyOf(GenreInsightSort sort) => sort switch
    {
        GenreInsightSort.AveragePopularityDesc => "average",
        GenreInsightSort.TrackCountDesc => "count",
        GenreInsightSort.GenreAsc => "genre",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Ordenação não suportada.")
    };

    private sealed record GenreRow(
        string Genre,
        double AveragePopularity,
        double MedianPopularity,
        long TrackCount,
        long ImputedTrackCount,
        long TotalCount);
}
