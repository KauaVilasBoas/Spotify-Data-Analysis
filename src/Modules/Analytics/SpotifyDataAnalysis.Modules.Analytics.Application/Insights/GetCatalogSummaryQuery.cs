using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

/// <summary>
/// Resumo do catálogo (E2.1) — a primeira query do read-side de Analytics e o esqueleto que os cards E2.2+
/// replicam. Entrega os números de visão geral que o dashboard (E5) usa como cartões de cabeçalho.
///
/// Convenção do read-side (documentada em <see cref="BaseDataAccess"/>): Query, QueryHandler e Result vivem
/// no MESMO arquivo. O handler herda <see cref="BaseDataAccess"/>, abre a conexão via
/// <see cref="BaseDataAccess.OpenConnectionAsync"/> e consulta com Dapper — nenhum EF Core no read-side.
///
/// Esta query não tem parâmetros nem paginação: é uma agregação única do catálogo inteiro. A convenção de
/// paginação por <c>ROW_NUMBER() + COUNT(*) OVER()</c> entra nas queries de listagem dos próximos cards.
/// </summary>
public sealed record GetCatalogSummaryQuery : IQuery<CatalogSummaryResult>;

/// <summary>
/// Resumo agregado do catálogo. Os campos de audio-features respeitam o default de domínio do E2: o
/// <b>medido</b> nunca inclui o <b>imputado</b> (RF3 / DP menor "a"), e o total com features é sempre
/// reportado para o consumidor conhecer o N por trás dos recortes.
/// </summary>
/// <param name="TotalTracks">Total de faixas no catálogo.</param>
/// <param name="TracksWithAudioFeatures">Faixas que possuem audio-features (coluna jsonb não nula).</param>
/// <param name="TracksWithoutAudioFeatures">Faixas sem audio-features (jsonb nulo) — o complemento do total.</param>
/// <param name="TracksWithMeasuredFeatures">Faixas com features MEDIDAS (presentes e não imputadas).</param>
/// <param name="TracksWithImputedFeatures">Faixas com features IMPUTADAS (preenchidas por tratamento de faltantes).</param>
/// <param name="DistinctArtists">Artistas distintos no catálogo (<c>catalog.artists</c>).</param>
/// <param name="DistinctAlbums">Álbuns distintos no catálogo (<c>catalog.albums</c>).</param>
/// <param name="DistinctGenres">Gêneros distintos declarados nas audio-features (<c>audio_features -&gt;&gt; 'Genre'</c>).</param>
public sealed record CatalogSummaryResult(
    long TotalTracks,
    long TracksWithAudioFeatures,
    long TracksWithoutAudioFeatures,
    long TracksWithMeasuredFeatures,
    long TracksWithImputedFeatures,
    long DistinctArtists,
    long DistinctAlbums,
    long DistinctGenres);

internal sealed class GetCatalogSummaryQueryHandler
    : BaseDataAccess, IQueryHandler<GetCatalogSummaryQuery, CatalogSummaryResult>
{
    // SQL do dialeto PostgreSQL (convenção do BaseDataAccess): identificadores minúsculos, sem colchetes,
    // sem WITH(NOLOCK). As audio-features vivem numa coluna jsonb "audio_features" (owned type serializado
    // por EF via ToJson) cujas CHAVES são os nomes das propriedades CLR do value object AudioFeatures —
    // por isso "IsImputed" e "Genre" em PascalCase (a convenção snake_case do DbContext pula owned types,
    // então NÃO renomeia as chaves do jsonb). Faixas sem features têm "audio_features" NULL.
    //
    // Recorte medido vs imputado (RF3 / critério de aceite): o imputado NUNCA conta como medido —
    //   medido  = audio_features presente E (IsImputed ausente OU false)
    //   imputado = audio_features presente E IsImputed = true
    // O COALESCE trata um IsImputed ausente no jsonb como "não imputado" (medido), sem quebrar a contagem.
    //
    // `internal` (não `private`) para o teste do módulo poder afirmar o CONTRATO do SQL — o recorte medido
    // vs imputado por `audio_features ->> 'IsImputed'` — sem exigir um Postgres vivo. Exposto ao assembly de
    // testes via InternalsVisibleTo; permanece invisível fora do módulo.
    internal const string Sql =
        """
        SELECT
            (SELECT COUNT(*) FROM catalog.tracks)                                          AS "TotalTracks",
            (SELECT COUNT(*) FROM catalog.tracks
              WHERE audio_features IS NOT NULL)                                            AS "TracksWithAudioFeatures",
            (SELECT COUNT(*) FROM catalog.tracks
              WHERE audio_features IS NULL)                                                AS "TracksWithoutAudioFeatures",
            (SELECT COUNT(*) FROM catalog.tracks
              WHERE audio_features IS NOT NULL
                AND COALESCE((audio_features ->> 'IsImputed')::boolean, false) = false)    AS "TracksWithMeasuredFeatures",
            (SELECT COUNT(*) FROM catalog.tracks
              WHERE audio_features IS NOT NULL
                AND COALESCE((audio_features ->> 'IsImputed')::boolean, false) = true)     AS "TracksWithImputedFeatures",
            (SELECT COUNT(*) FROM catalog.artists)                                         AS "DistinctArtists",
            (SELECT COUNT(*) FROM catalog.albums)                                          AS "DistinctAlbums",
            (SELECT COUNT(DISTINCT audio_features ->> 'Genre') FROM catalog.tracks
              WHERE audio_features ->> 'Genre' IS NOT NULL)                                AS "DistinctGenres";
        """;

    public GetCatalogSummaryQueryHandler(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<CatalogSummaryResult> HandleAsync(
        GetCatalogSummaryQuery request, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        var command = new CommandDefinition(Sql, cancellationToken: cancellationToken);

        return await connection.QuerySingleAsync<CatalogSummaryResult>(command);
    }
}
