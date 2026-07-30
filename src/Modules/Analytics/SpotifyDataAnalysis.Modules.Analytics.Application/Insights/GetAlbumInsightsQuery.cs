using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

/// <summary>
/// Ranking de álbuns pela popularidade média das faixas que o catálogo tem daquele álbum, paginado.
/// </summary>
/// <remarks>
/// Agrupa por <c>tracks.album_id</c> e busca nome e ano em <c>catalog.albums</c> por LEFT JOIN, então um álbum
/// referenciado por uma faixa mas ainda não registrado aparece com nome nulo em vez de sumir do ranking.
/// Faixas sem <c>album_id</c> ficam fora: aqui a linha É um álbum, e ausência de álbum não é um álbum — ao
/// contrário do gênero, onde a ausência é uma categoria legítima e ganha bucket próprio. Com playlist-semente
/// o número de faixas por álbum é baixo, então <c>TrackCount</c> importa para ler a média sem se enganar.
/// </remarks>
public sealed record GetAlbumInsightsQuery : PagedQuery<PagedResult<AlbumInsightItem>>
{
    /// <summary>Critério de ordenação.</summary>
    public AlbumInsightSort Sort { get; init; } = AlbumInsightSort.AveragePopularityDesc;
}

/// <summary>Ordenações suportadas pelo ranking de álbuns.</summary>
public enum AlbumInsightSort
{
    AveragePopularityDesc,
    TrackCountDesc,
    NameAsc
}

/// <summary>
/// Um álbum no ranking. <paramref name="Name"/> e <paramref name="ReleaseYear"/> são nulos quando o álbum
/// referenciado pelas faixas ainda não foi registrado ou não teve a data de lançamento informada.
/// </summary>
public sealed record AlbumInsightItem(
    string AlbumId,
    string? Name,
    int? ReleaseYear,
    double AveragePopularity,
    long TrackCount);

internal sealed class GetAlbumInsightsQueryHandler
    : BaseDataAccess, IQueryHandler<GetAlbumInsightsQuery, PagedResult<AlbumInsightItem>>
{
    internal const string Sql =
        """
        WITH grouped AS (
            SELECT
                t.album_id                          AS album_id,
                AVG(t.popularity)::double precision AS average_popularity,
                COUNT(*)                            AS track_count
            FROM catalog.tracks AS t
            WHERE t.album_id IS NOT NULL
            GROUP BY t.album_id
        ),
        records AS (
            SELECT
                g.album_id,
                al.name             AS album_name,
                al.release_year     AS release_year,
                g.average_popularity,
                g.track_count,
                ROW_NUMBER() OVER (
                    ORDER BY
                        CASE WHEN @SortBy = 'average' THEN g.average_popularity END DESC,
                        CASE WHEN @SortBy = 'count' THEN g.track_count END DESC,
                        CASE WHEN @SortBy = 'name' THEN al.name END ASC,
                        g.album_id ASC
                )        AS row_number,
                COUNT(*) OVER () AS total_count
            FROM grouped AS g
            LEFT JOIN catalog.albums AS al ON al.id = g.album_id
        )
        SELECT
            album_id            AS "AlbumId",
            album_name          AS "Name",
            release_year        AS "ReleaseYear",
            average_popularity  AS "AveragePopularity",
            track_count         AS "TrackCount",
            total_count         AS "TotalCount"
        FROM records
        WHERE row_number BETWEEN @FirstResult AND @LastResult
        ORDER BY row_number;
        """;

    public GetAlbumInsightsQueryHandler(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<PagedResult<AlbumInsightItem>> HandleAsync(
        GetAlbumInsightsQuery request, CancellationToken cancellationToken = default)
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

        IReadOnlyList<AlbumRow> rows = (await connection.QueryAsync<AlbumRow>(command)).AsList();

        int totalCount = rows.Count > 0 ? (int)rows[0].TotalCount : 0;

        IReadOnlyList<AlbumInsightItem> items = rows
            .Select(row => new AlbumInsightItem(
                row.AlbumId, row.Name, row.ReleaseYear, row.AveragePopularity, row.TrackCount))
            .ToArray();

        return new PagedResult<AlbumInsightItem>(items, totalCount, request.Page, request.PageSize);
    }

    internal static string SortKeyOf(AlbumInsightSort sort) => sort switch
    {
        AlbumInsightSort.AveragePopularityDesc => "average",
        AlbumInsightSort.TrackCountDesc => "count",
        AlbumInsightSort.NameAsc => "name",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Ordenação não suportada.")
    };

    private sealed record AlbumRow(
        string AlbumId,
        string? Name,
        int? ReleaseYear,
        double AveragePopularity,
        long TrackCount,
        long TotalCount);
}
