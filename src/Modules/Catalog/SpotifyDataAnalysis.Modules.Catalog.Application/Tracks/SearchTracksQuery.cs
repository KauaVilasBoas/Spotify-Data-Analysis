using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Tracks;

/// <summary>
/// Listagem/busca paginada de faixas do catálogo. Primeiro read-side do módulo Catalog: as rotas de leitura de
/// catálogo ficam aqui, e não no Analytics, que responde só pelos insights agregados.
/// </summary>
public sealed record SearchTracksQuery : PagedQuery<PagedResult<TrackListItem>>
{
    /// <summary>
    /// Termo de busca. Casa por substring sem diferenciar maiúsculas contra o nome da faixa ou o nome de
    /// qualquer artista creditado. Nulo/vazio cobre o catálogo inteiro.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Critério de ordenação.</summary>
    public TrackSort Sort { get; init; } = TrackSort.Name;
}

/// <summary>
/// Ordenações suportadas pela listagem. Enum fechado porque o critério chega pela query string e o SQL escolhe
/// o ramo por parâmetro, sem montar <c>ORDER BY</c> a partir do valor recebido.
/// </summary>
public enum TrackSort
{
    Name,
    PopularityDesc
}

/// <summary>
/// Uma faixa na listagem. <paramref name="HasAudioFeatures"/> permite à tela sinalizar quais faixas já têm dado
/// de EDA sem uma chamada por linha; as features em si saem no detalhe.
/// </summary>
public sealed record TrackListItem(
    string TrackId,
    string Name,
    string? PrimaryArtist,
    int Popularity,
    int DurationMs,
    bool Explicit,
    string? AlbumId,
    bool HasAudioFeatures);

internal sealed class SearchTracksQueryHandler
    : BaseDataAccess, IQueryHandler<SearchTracksQuery, PagedResult<TrackListItem>>
{
    internal const string Sql =
        """
        WITH records AS (
            SELECT
                t.id                                        AS "TrackId",
                t.name                                      AS "Name",
                t.artists -> 0 ->> 'Name'                   AS "PrimaryArtist",
                t.popularity                                AS "Popularity",
                t.duration_ms                               AS "DurationMs",
                t.explicit                                  AS "Explicit",
                t.album_id                                  AS "AlbumId",
                (t.audio_features IS NOT NULL)              AS "HasAudioFeatures",
                ROW_NUMBER() OVER (
                    ORDER BY
                        CASE WHEN @SortByPopularity THEN t.popularity END DESC,
                        CASE WHEN @SortByPopularity THEN NULL ELSE t.name END ASC,
                        t.id ASC
                )                                           AS row_number,
                COUNT(*) OVER ()                            AS total_count
            FROM catalog.tracks AS t
            WHERE (
                @Search IS NULL
                OR t.name ILIKE '%' || @Search || '%'
                OR EXISTS (
                    SELECT 1
                    FROM jsonb_array_elements(t.artists) AS credit
                    WHERE credit ->> 'Name' ILIKE '%' || @Search || '%'
                )
            )
        )
        SELECT
            "TrackId", "Name", "PrimaryArtist", "Popularity", "DurationMs", "Explicit", "AlbumId",
            "HasAudioFeatures", total_count AS "TotalCount"
        FROM records
        WHERE row_number BETWEEN @FirstResult AND @LastResult
        ORDER BY row_number;
        """;

    public SearchTracksQueryHandler(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<PagedResult<TrackListItem>> HandleAsync(
        SearchTracksQuery request, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        string? search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        var command = new CommandDefinition(
            Sql,
            new
            {
                Search = search,
                SortByPopularity = request.Sort == TrackSort.PopularityDesc,
                request.FirstResult,
                request.LastResult
            },
            cancellationToken: cancellationToken);

        IReadOnlyList<TrackRow> rows = (await connection.QueryAsync<TrackRow>(command)).AsList();

        int totalCount = rows.Count > 0 ? (int)rows[0].TotalCount : 0;

        IReadOnlyList<TrackListItem> items = rows
            .Select(row => new TrackListItem(
                row.TrackId, row.Name, row.PrimaryArtist, row.Popularity, row.DurationMs,
                row.Explicit, row.AlbumId, row.HasAudioFeatures))
            .ToArray();

        return new PagedResult<TrackListItem>(items, totalCount, request.Page, request.PageSize);
    }

    /// <summary>
    /// Linha crua do Dapper. <c>TotalCount</c> é <c>long</c> porque <c>COUNT(*) OVER()</c> devolve
    /// <c>bigint</c>: um <c>int</c> aqui faz a materialização falhar em runtime, contra banco real.
    /// </summary>
    private sealed record TrackRow(
        string TrackId,
        string Name,
        string? PrimaryArtist,
        int Popularity,
        int DurationMs,
        bool Explicit,
        string? AlbumId,
        bool HasAudioFeatures,
        long TotalCount);
}
