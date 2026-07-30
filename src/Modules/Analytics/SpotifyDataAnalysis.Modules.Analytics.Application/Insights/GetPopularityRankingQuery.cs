using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

/// <summary>
/// Ranking de popularidade (E2.2): as faixas mais populares do catálogo, ordenadas por
/// <c>popularity</c> decrescente e paginadas. É o primeiro insight paginado do read-side e exercita a
/// convenção de paginação <c>ROW_NUMBER() + COUNT(*) OVER()</c> documentada em <see cref="BaseDataAccess"/>.
///
/// Convenção do read-side: Query, QueryHandler, Result e ResultItem vivem no MESMO arquivo. O handler herda
/// <see cref="BaseDataAccess"/>, abre a conexão via <see cref="BaseDataAccess.OpenConnectionAsync"/> e
/// consulta com Dapper — nenhum EF Core no read-side.
///
/// A herança de <see cref="PagedQuery{TResult}"/> traz <c>Page</c>/<c>PageSize</c> (com clamp em 200) e os
/// limites 1-based <c>FirstResult</c>/<c>LastResult</c> usados no <c>WHERE row_number BETWEEN</c>.
/// </summary>
/// <remarks>
/// Filtro por gênero (DP-1 do card, resolvida): <see cref="Genre"/> é OPCIONAL. Quando ausente, o ranking
/// cobre o catálogo INTEIRO — inclusive faixas sem audio-features (que não têm gênero), porque popularidade
/// não depende de features. Quando informado, restringe a faixas cujo <c>audio_features -&gt;&gt; 'Genre'</c>
/// bate com o valor, o que naturalmente exclui as faixas de <c>audio_features</c> nulo.
/// </remarks>
public sealed record GetPopularityRankingQuery : PagedQuery<PagedResult<PopularityRankingItem>>
{
    /// <summary>Gênero (das audio-features) para restringir o ranking. Nulo/vazio ⇒ catálogo inteiro.</summary>
    public string? Genre { get; init; }
}

/// <summary>
/// Uma faixa no ranking de popularidade.
/// </summary>
/// <param name="TrackId">Id da faixa no Spotify (<c>catalog.tracks.id</c>).</param>
/// <param name="Name">Nome da faixa.</param>
/// <param name="PrimaryArtist">Nome do artista principal (1º crédito do array jsonb <c>artists</c>).</param>
/// <param name="Popularity">Popularidade Spotify (0–100).</param>
/// <param name="Genre">Gênero das audio-features, quando a faixa tem features; nulo caso contrário.</param>
public sealed record PopularityRankingItem(
    string TrackId,
    string Name,
    string? PrimaryArtist,
    int Popularity,
    string? Genre);

internal sealed class GetPopularityRankingQueryHandler
    : BaseDataAccess, IQueryHandler<GetPopularityRankingQuery, PagedResult<PopularityRankingItem>>
{
    // SQL do dialeto PostgreSQL (convenção do BaseDataAccess): identificadores minúsculos/aspas duplas, sem
    // colchetes, sem WITH(NOLOCK). Extrações do jsonb (chaves em PascalCase — o snake_case do DbContext pula
    // owned types e o campo de apoio, então NÃO renomeia as chaves internas):
    //   - artista principal: "artists -> 0 ->> 'Name'"  (1º elemento do array; a ordem preserva a da API)
    //   - gênero:            "audio_features ->> 'Genre'" (nulo quando a faixa não tem features)
    //
    // Paginação (padrão documentado em BaseDataAccess): CTE com ROW_NUMBER() OVER(ORDER BY ...) para o
    // recorte da página e COUNT(*) OVER() para o total — num ÚNICO round-trip. A ordenação inclui o desempate
    // determinístico por "id" para que a paginação seja estável (a mesma faixa nunca aparece em duas páginas
    // quando há empate de popularity).
    //
    // Filtro por gênero (DP-1): "(@Genre IS NULL OR audio_features ->> 'Genre' = @Genre)". Sem @Genre o
    // predicado é verdadeiro para todas as faixas (inclusive as de audio_features nulo); com @Genre só passam
    // as que têm o gênero, descartando naturalmente as sem features.
    //
    // `internal` (não `private`) para o teste do módulo afirmar o CONTRATO do SQL (recorte por gênero,
    // ordenação estável, dialeto Postgres) sem exigir um Postgres vivo. Exposto ao assembly de testes via
    // InternalsVisibleTo; permanece invisível fora do módulo.
    internal const string Sql =
        """
        WITH records AS (
            SELECT
                t.id                                                                  AS "TrackId",
                t.name                                                                AS "Name",
                t.artists -> 0 ->> 'Name'                                             AS "PrimaryArtist",
                t.popularity                                                          AS "Popularity",
                t.audio_features ->> 'Genre'                                          AS "Genre",
                ROW_NUMBER() OVER (ORDER BY t.popularity DESC, t.id ASC)              AS row_number,
                COUNT(*)     OVER ()                                                  AS total_count
            FROM catalog.tracks AS t
            WHERE (@Genre IS NULL OR t.audio_features ->> 'Genre' = @Genre)
        )
        SELECT "TrackId", "Name", "PrimaryArtist", "Popularity", "Genre", total_count AS "TotalCount"
        FROM records
        WHERE row_number BETWEEN @FirstResult AND @LastResult
        ORDER BY row_number;
        """;

    public GetPopularityRankingQueryHandler(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<PagedResult<PopularityRankingItem>> HandleAsync(
        GetPopularityRankingQuery request, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        // Normaliza o gênero para o padrão do domínio (o Genre é gravado no jsonb em lowercase/trim pelo
        // AudioFeatures.Create), de modo que o filtro case com o dado persistido independente da caixa que o
        // cliente enviou. Vazio/whitespace vira NULL ⇒ sem filtro.
        string? genre = string.IsNullOrWhiteSpace(request.Genre)
            ? null
            : request.Genre.Trim().ToLowerInvariant();

        var command = new CommandDefinition(
            Sql,
            new { Genre = genre, request.FirstResult, request.LastResult },
            cancellationToken: cancellationToken);

        IReadOnlyList<RankingRow> rows = (await connection.QueryAsync<RankingRow>(command)).AsList();

        int totalCount = rows.Count > 0 ? rows[0].TotalCount : 0;

        IReadOnlyList<PopularityRankingItem> items = rows
            .Select(row => new PopularityRankingItem(
                row.TrackId, row.Name, row.PrimaryArtist, row.Popularity, row.Genre))
            .ToArray();

        return new PagedResult<PopularityRankingItem>(items, totalCount, request.Page, request.PageSize);
    }

    // Linha crua do Dapper: o item de ranking + o TotalCount do COUNT(*) OVER() (repetido em cada linha da
    // página). Materializado no handler para o PagedResult carregar o total num só round-trip.
    private sealed record RankingRow(
        string TrackId,
        string Name,
        string? PrimaryArtist,
        int Popularity,
        string? Genre,
        int TotalCount);
}
