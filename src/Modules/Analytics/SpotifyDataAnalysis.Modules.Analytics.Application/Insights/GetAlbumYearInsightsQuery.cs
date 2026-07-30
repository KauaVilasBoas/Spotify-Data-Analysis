using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

/// <summary>
/// Popularidade média por ano de lançamento — a leitura de tendência temporal do catálogo. Agrupa por
/// <c>albums.release_year</c>, que é coluna própria indexada justamente para este recorte.
/// </summary>
/// <remarks>
/// Faixas cujo álbum não tem ano conhecido (ou que não têm álbum) não desaparecem: caem numa linha de
/// <c>Year</c> nulo, para a soma das contagens fechar com o total do catálogo.
/// </remarks>
public sealed record GetAlbumYearInsightsQuery : PagedQuery<PagedResult<AlbumYearInsightItem>>
{
    /// <summary>Critério de ordenação.</summary>
    public AlbumYearInsightSort Sort { get; init; } = AlbumYearInsightSort.YearDesc;
}

/// <summary>Ordenações suportadas pelo recorte por ano.</summary>
public enum AlbumYearInsightSort
{
    YearDesc,
    YearAsc,
    AveragePopularityDesc
}

/// <summary>
/// Um ano de lançamento no recorte. <paramref name="Year"/> nulo agrupa as faixas sem ano conhecido.
/// </summary>
public sealed record AlbumYearInsightItem(
    int? Year,
    double AveragePopularity,
    long TrackCount,
    long AlbumCount);

internal sealed class GetAlbumYearInsightsQueryHandler
    : BaseDataAccess, IQueryHandler<GetAlbumYearInsightsQuery, PagedResult<AlbumYearInsightItem>>
{
    internal const string Sql =
        """
        WITH grouped AS (
            SELECT
                al.release_year                        AS year,
                AVG(t.popularity)::double precision    AS average_popularity,
                COUNT(*)                               AS track_count,
                COUNT(DISTINCT t.album_id)             AS album_count
            FROM catalog.tracks AS t
            LEFT JOIN catalog.albums AS al ON al.id = t.album_id
            GROUP BY al.release_year
        ),
        records AS (
            SELECT
                year,
                average_popularity,
                track_count,
                album_count,
                ROW_NUMBER() OVER (
                    ORDER BY
                        CASE WHEN @SortBy = 'average' THEN average_popularity END DESC,
                        CASE WHEN @SortBy = 'year_asc' THEN year END ASC NULLS LAST,
                        CASE WHEN @SortBy = 'year_desc' THEN year END DESC NULLS LAST,
                        year ASC NULLS LAST
                )        AS row_number,
                COUNT(*) OVER () AS total_count
            FROM grouped
        )
        SELECT
            year                AS "Year",
            average_popularity  AS "AveragePopularity",
            track_count         AS "TrackCount",
            album_count         AS "AlbumCount",
            total_count         AS "TotalCount"
        FROM records
        WHERE row_number BETWEEN @FirstResult AND @LastResult
        ORDER BY row_number;
        """;

    public GetAlbumYearInsightsQueryHandler(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<PagedResult<AlbumYearInsightItem>> HandleAsync(
        GetAlbumYearInsightsQuery request, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        var command = new CommandDefinition(
            Sql,
            new
            {
                SortBy = SortKeyOf(request.Sort),
                request.FirstResult,
                request.LastResult
            },
            cancellationToken: cancellationToken);

        IReadOnlyList<YearRow> rows = (await connection.QueryAsync<YearRow>(command)).AsList();

        int totalCount = rows.Count > 0 ? (int)rows[0].TotalCount : 0;

        IReadOnlyList<AlbumYearInsightItem> items = rows
            .Select(row => new AlbumYearInsightItem(
                row.Year, row.AveragePopularity, row.TrackCount, row.AlbumCount))
            .ToArray();

        return new PagedResult<AlbumYearInsightItem>(items, totalCount, request.Page, request.PageSize);
    }

    internal static string SortKeyOf(AlbumYearInsightSort sort) => sort switch
    {
        AlbumYearInsightSort.YearDesc => "year_desc",
        AlbumYearInsightSort.YearAsc => "year_asc",
        AlbumYearInsightSort.AveragePopularityDesc => "average",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Ordenação não suportada.")
    };

    private sealed record YearRow(
        int? Year,
        double AveragePopularity,
        long TrackCount,
        long AlbumCount,
        long TotalCount);
}
