using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations.Evaluation;

/// <summary>
/// Monta o insumo da avaliação do recomendador (E4.4) lendo o schema <c>catalog</c> com Dapper: a amostra FIXA de
/// sementes, os grupos de quase-duplicatas e o censo que contextualiza os números. Mesma fronteira do E3.1/E4.1 —
/// SQL puro, zero referência .NET a tipos do Catalog (posição guardada pelos ArchTests).
///
/// <para><b>Reprodutibilidade por SQL, não por Random:</b> a amostragem ordena por <c>md5(semente || chave)</c>.
/// É determinística (a mesma semente textual devolve sempre as mesmas faixas), independe de cultura, de versão do
/// runtime e da ordem física da tabela, e — ao contrário de sortear em memória — nunca materializa o catálogo
/// inteiro no processo, o que a restrição de free tier proíbe.</para>
///
/// <para><b>Sem porta/interface de propósito:</b> o avaliador do domínio não depende desta classe (recebe a amostra
/// pronta), então uma interface aqui só existiria para ser implementada uma vez — cerimônia sem ganho de
/// testabilidade.</para>
/// </summary>
internal sealed class CatalogRecommenderEvaluationSampleSource : BaseDataAccess
{
    /// <summary>
    /// O predicado de ELEGIBILIDADE, idêntico em regra ao <c>WHERE</c> do <see cref="CatalogSimilarityFeatureSource"/>:
    /// só faixa com as NOVE features contínuas presentes entra no índice, logo amostrar de um universo mais amplo
    /// produziria sementes que o avaliador só teria como descartar. Restatado aqui (e não extraído do SQL do E4.1)
    /// para não reescrever uma consulta de produção já validada em smoke por conveniência cosmética.
    /// </summary>
    private const string EligibilityPredicate =
        """
              (t.audio_features ->> 'Danceability')     IS NOT NULL
          AND (t.audio_features ->> 'Energy')           IS NOT NULL
          AND (t.audio_features ->> 'Valence')          IS NOT NULL
          AND (t.audio_features ->> 'Tempo')            IS NOT NULL
          AND (t.audio_features ->> 'Acousticness')     IS NOT NULL
          AND (t.audio_features ->> 'Instrumentalness') IS NOT NULL
          AND (t.audio_features ->> 'Liveness')         IS NOT NULL
          AND (t.audio_features ->> 'Speechiness')      IS NOT NULL
          AND (t.audio_features ->> 'Loudness')         IS NOT NULL
        """;

    /// <summary>Sementes sorteadas de forma determinística entre as faixas elegíveis.</summary>
    internal const string SeedSampleSql =
        $"""
        SELECT t.id
        FROM catalog.tracks AS t
        WHERE {EligibilityPredicate}
        ORDER BY md5(@Seed || t.id)
        LIMIT @SampleSize;
        """;

    /// <summary>
    /// Grupos de quase-duplicatas por <c>match_key</c>, sorteados deterministicamente, com os membros limitados por
    /// <c>ROW_NUMBER()</c>. O teto de membros existe porque o catálogo tem grupos de dezenas de faixas (coletâneas
    /// natalinas): sem ele, um punhado de grupos gigantes dominaria a média e o custo da varredura.
    /// </summary>
    internal const string DuplicateGroupSampleSql =
        $"""
        WITH grouped AS (
            SELECT t.match_key AS match_key
            FROM catalog.tracks AS t
            WHERE {EligibilityPredicate}
            GROUP BY t.match_key
            HAVING count(*) > 1
        ),
        sampled AS (
            SELECT g.match_key
            FROM grouped AS g
            ORDER BY md5(@Seed || g.match_key)
            LIMIT @GroupCount
        ),
        members AS (
            SELECT t.match_key AS match_key,
                   t.id        AS track_id,
                   ROW_NUMBER() OVER (PARTITION BY t.match_key ORDER BY t.id) AS member_rank
            FROM catalog.tracks AS t
            JOIN sampled AS s ON s.match_key = t.match_key
            WHERE {EligibilityPredicate}
        )
        SELECT m.match_key AS "MatchKey",
               m.track_id  AS "TrackId"
        FROM members AS m
        WHERE m.member_rank <= @MaxMembersPerGroup
        ORDER BY m.match_key, m.track_id;
        """;

    /// <summary>
    /// O censo que contextualiza os proxies: tamanho do universo elegível, quantas faixas são imputadas (DP-F) e o
    /// dimensionamento das duplicatas — grupos, faixas envolvidas, pares e maior grupo.
    ///
    /// <para><b>Tipos numéricos do PostgreSQL, explicitamente:</b> <c>count(*)</c> devolve <c>bigint</c> (ler em
    /// <c>int</c> foi um bug real deste projeto no E2.2), e <c>sum(bigint)</c> devolve <c>numeric</c> — que
    /// materializaria em <c>decimal</c> e quebraria a leitura em <c>long</c>. Por isso os <c>sum</c> levam
    /// <c>::bigint</c> explícito: o cast está no SQL, onde a promoção de tipo acontece, e não escondido num tipo
    /// C# frouxo.</para>
    /// </summary>
    internal const string CensusSql =
        $"""
        WITH eligible AS (
            SELECT t.match_key AS match_key,
                   COALESCE((t.audio_features ->> 'IsImputed')::boolean, false) AS is_imputed
            FROM catalog.tracks AS t
            WHERE {EligibilityPredicate}
        ),
        grouped AS (
            SELECT e.match_key, count(*) AS member_count
            FROM eligible AS e
            GROUP BY e.match_key
            HAVING count(*) > 1
        )
        SELECT (SELECT count(*) FROM eligible)                                          AS "EligibleTracks",
               (SELECT count(*) FROM eligible AS e WHERE e.is_imputed)                  AS "ImputedTracks",
               (SELECT count(*) FROM grouped)                                           AS "DuplicateGroups",
               (SELECT COALESCE(sum(g.member_count), 0)::bigint FROM grouped AS g)      AS "DuplicatedTracks",
               (SELECT COALESCE(sum(g.member_count * (g.member_count - 1) / 2), 0)::bigint
                  FROM grouped AS g)                                                    AS "DuplicatePairs",
               (SELECT COALESCE(max(g.member_count), 0) FROM grouped AS g)              AS "LargestGroup";
        """;

    public CatalogRecommenderEvaluationSampleSource(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    /// <summary>
    /// Lê a amostra completa da avaliação numa única conexão: as sementes e os grupos de duplicatas, ambos
    /// determinados pela mesma <paramref name="samplingSeed"/> textual.
    /// </summary>
    /// <param name="samplingSeed">Semente textual que fixa o sorteio — trocá-la troca a amostra.</param>
    /// <param name="seedSampleSize">Quantas sementes sortear para os proxies 1 e 2.</param>
    /// <param name="duplicateGroupCount">Quantos grupos de duplicatas sortear para o proxy 3.</param>
    /// <param name="maxMembersPerGroup">Teto de membros considerados por grupo.</param>
    public async Task<RecommenderEvaluationSample> LoadSampleAsync(
        string samplingSeed,
        int seedSampleSize,
        int duplicateGroupCount,
        int maxMembersPerGroup,
        CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        var seedCommand = new CommandDefinition(
            SeedSampleSql,
            new { Seed = samplingSeed, SampleSize = seedSampleSize },
            cancellationToken: cancellationToken);

        string[] seedTrackIds = (await connection.QueryAsync<string>(seedCommand)).ToArray();

        var groupCommand = new CommandDefinition(
            DuplicateGroupSampleSql,
            new { Seed = samplingSeed, GroupCount = duplicateGroupCount, MaxMembersPerGroup = maxMembersPerGroup },
            cancellationToken: cancellationToken);

        DuplicateMemberRow[] memberRows =
            (await connection.QueryAsync<DuplicateMemberRow>(groupCommand)).ToArray();

        var groups = new List<DuplicateTrackGroup>();
        foreach (IGrouping<string, DuplicateMemberRow> grouped in
            memberRows.GroupBy(row => row.MatchKey, StringComparer.Ordinal))
        {
            string[] trackIds = grouped.Select(row => row.TrackId).ToArray();

            if (trackIds.Length >= 2)
                groups.Add(DuplicateTrackGroup.Create(grouped.Key, trackIds));
        }

        return RecommenderEvaluationSample.Create(seedTrackIds, groups);
    }

    /// <summary>Lê o censo do catálogo elegível e das duplicatas — os números que contextualizam os proxies e dimensionam o E4.7.</summary>
    public async Task<CatalogDuplicateCensus> LoadCensusAsync(CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        var command = new CommandDefinition(CensusSql, cancellationToken: cancellationToken);

        return await connection.QuerySingleAsync<CatalogDuplicateCensus>(command);
    }

    private sealed record DuplicateMemberRow(string MatchKey, string TrackId);
}

/// <summary>
/// O censo do catálogo do ponto de vista da avaliação. Todas as contagens são <c>long</c> porque
/// <c>count(*)</c>/<c>sum(...)</c> do PostgreSQL são <c>bigint</c> — materializá-las em <c>int</c> quebra na
/// leitura, e foi assim que um bug passou por 18 testes verdes no E2.2.
/// </summary>
/// <param name="EligibleTracks">Faixas com as nove features presentes — o universo do índice.</param>
/// <param name="ImputedTracks">Faixas com features imputadas (DP-F).</param>
/// <param name="DuplicateGroups">Grupos de <c>match_key</c> com mais de uma faixa.</param>
/// <param name="DuplicatedTracks">Faixas que pertencem a algum grupo de duplicatas.</param>
/// <param name="DuplicatePairs">Pares distintos de quase-duplicatas — o número que dimensiona o E4.7.</param>
/// <param name="LargestGroup">Tamanho do maior grupo.</param>
public sealed record CatalogDuplicateCensus(
    long EligibleTracks,
    long ImputedTracks,
    long DuplicateGroups,
    long DuplicatedTracks,
    long DuplicatePairs,
    long LargestGroup);
