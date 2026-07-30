using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

/// <summary>
/// Ranking de artistas pela popularidade e seguidores do PRÓPRIO artista, lidos de <c>catalog.artists</c> —
/// não pela média das faixas dele. É o sinal barato (sem explodir o jsonb de créditos) e o mesmo que o E3
/// consome como feature.
/// </summary>
/// <remarks>
/// Artistas ainda não enriquecidos têm <c>popularity</c> e <c>followers</c> zerados, o que poluiria o ranking
/// com uma cauda de zeros; por isso ficam de fora por padrão. Não é omissão silenciosa:
/// <c>includeUnenriched=true</c> os traz, e cada item carrega <c>IsEnriched</c>.
/// </remarks>
public sealed record GetArtistInsightsQuery : PagedQuery<PagedResult<ArtistInsightItem>>
{
    /// <summary>Critério de ordenação.</summary>
    public ArtistInsightSort Sort { get; init; } = ArtistInsightSort.PopularityDesc;

    /// <summary>Se artistas sem enriquecimento (popularidade/seguidores zerados) entram no ranking.</summary>
    public bool IncludeUnenriched { get; init; }
}

/// <summary>Ordenações suportadas pelo ranking de artistas.</summary>
public enum ArtistInsightSort
{
    PopularityDesc,
    FollowersDesc,
    NameAsc
}

/// <summary>
/// Um artista no ranking. <paramref name="IsEnriched"/> diz se <paramref name="Popularity"/> e
/// <paramref name="Followers"/> são dados reais da API ou os zeros de um artista ainda não enriquecido.
/// </summary>
public sealed record ArtistInsightItem(
    string ArtistId,
    string Name,
    int Popularity,
    int Followers,
    bool IsEnriched);

internal sealed class GetArtistInsightsQueryHandler
    : BaseDataAccess, IQueryHandler<GetArtistInsightsQuery, PagedResult<ArtistInsightItem>>
{
    internal const string Sql =
        """
        WITH records AS (
            SELECT
                a.id            AS "ArtistId",
                a.name          AS "Name",
                a.popularity    AS "Popularity",
                a.followers     AS "Followers",
                a.is_enriched   AS "IsEnriched",
                ROW_NUMBER() OVER (
                    ORDER BY
                        CASE WHEN @SortBy = 'popularity' THEN a.popularity END DESC,
                        CASE WHEN @SortBy = 'followers' THEN a.followers END DESC,
                        CASE WHEN @SortBy = 'name' THEN a.name END ASC,
                        a.id ASC
                )        AS row_number,
                COUNT(*) OVER () AS total_count
            FROM catalog.artists AS a
            WHERE (@IncludeUnenriched OR a.is_enriched)
        )
        SELECT
            "ArtistId", "Name", "Popularity", "Followers", "IsEnriched",
            total_count AS "TotalCount"
        FROM records
        WHERE row_number BETWEEN @FirstResult AND @LastResult
        ORDER BY row_number;
        """;

    public GetArtistInsightsQueryHandler(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<PagedResult<ArtistInsightItem>> HandleAsync(
        GetArtistInsightsQuery request, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        var command = new CommandDefinition(
            Sql,
            new
            {
                SortBy = SortKeyOf(request.Sort),
                request.IncludeUnenriched,
                request.FirstResult,
                request.LastResult
            },
            cancellationToken: cancellationToken);

        IReadOnlyList<ArtistRow> rows = (await connection.QueryAsync<ArtistRow>(command)).AsList();

        int totalCount = rows.Count > 0 ? (int)rows[0].TotalCount : 0;

        IReadOnlyList<ArtistInsightItem> items = rows
            .Select(row => new ArtistInsightItem(
                row.ArtistId, row.Name, row.Popularity, row.Followers, row.IsEnriched))
            .ToArray();

        return new PagedResult<ArtistInsightItem>(items, totalCount, request.Page, request.PageSize);
    }

    internal static string SortKeyOf(ArtistInsightSort sort) => sort switch
    {
        ArtistInsightSort.PopularityDesc => "popularity",
        ArtistInsightSort.FollowersDesc => "followers",
        ArtistInsightSort.NameAsc => "name",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Ordenação não suportada.")
    };

    private sealed record ArtistRow(
        string ArtistId,
        string Name,
        int Popularity,
        int Followers,
        bool IsEnriched,
        long TotalCount);
}
