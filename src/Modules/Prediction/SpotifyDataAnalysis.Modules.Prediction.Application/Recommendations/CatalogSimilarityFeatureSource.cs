using System.Data;
using System.Runtime.CompilerServices;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

/// <summary>
/// Leitura das faixas elegíveis do schema <c>catalog</c> com Dapper, herdando <see cref="BaseDataAccess"/>, para
/// montar o índice de similaridade (E4.1). Acoplamento por DADO idêntico ao do E3.1: lê a tabela do Catalog por
/// SQL, sem referenciar tipo .NET algum daquele módulo — posição guardada pelos ArchTests.
///
/// <para><b>Extração idêntica à do E3.1</b> (mesmas chaves jsonb PascalCase, mesmo cast <c>::double precision</c>):
/// as audio-features são as propriedades CLR do value object do Catalog serializadas como JSON, e ler por uma
/// projeção diferente da do treino/inferência seria abrir a porta do skew justamente na leitura.</para>
///
/// <para><b>Elegibilidade no <c>WHERE</c>:</b> só entram faixas cujas NOVE features contínuas estão presentes
/// (<c>IS NOT NULL</c>). É o SQL, e não o domínio, que barra a faixa sem insumo — assim uma faixa sem
/// <c>audio_features</c> (ou com o jsonb incompleto) nunca chega ao índice, cumprindo "faixa sem features nunca
/// entra silenciosamente". <b>Paginação por keyset</b> (<c>id &gt; @AfterId ORDER BY id LIMIT @BatchSize</c>),
/// não <c>OFFSET</c>: custo constante por lote ao varrer a tabela inteira e teto de memória no tamanho do lote.</para>
/// </summary>
internal sealed class CatalogSimilarityFeatureSource : BaseDataAccess, ISimilarityFeatureSource
{
    /// <summary>Menor lote aceito, para o keyset não degenerar em uma ida ao banco por faixa.</summary>
    internal const int MinimumBatchSize = 100;

    /// <summary>Maior lote aceito, para o result set de um lote não virar o problema que a paginação evita.</summary>
    internal const int MaximumBatchSize = 50_000;

    /// <summary>
    /// Projeção das faixas ELEGÍVEIS: só as nove features contínuas (as que compõem o vetor), a identidade e a
    /// marca de imputação. Sem popularidade, sem duração, sem gênero — nada disso é eixo de similaridade no E4.1.
    /// O <c>WHERE</c> exige as nove presentes, então nenhuma coordenada chega nula ao domínio.
    /// </summary>
    internal const string Sql =
        """
        SELECT
            t.id                                                            AS "TrackId",
            COALESCE((t.audio_features ->> 'IsImputed')::boolean, false)    AS "IsImputed",
            (t.audio_features ->> 'Danceability')::double precision         AS "Danceability",
            (t.audio_features ->> 'Energy')::double precision               AS "Energy",
            (t.audio_features ->> 'Valence')::double precision              AS "Valence",
            (t.audio_features ->> 'Tempo')::double precision                AS "Tempo",
            (t.audio_features ->> 'Acousticness')::double precision         AS "Acousticness",
            (t.audio_features ->> 'Instrumentalness')::double precision     AS "Instrumentalness",
            (t.audio_features ->> 'Liveness')::double precision             AS "Liveness",
            (t.audio_features ->> 'Speechiness')::double precision          AS "Speechiness",
            (t.audio_features ->> 'Loudness')::double precision             AS "Loudness"
        FROM catalog.tracks AS t
        WHERE t.id > @AfterId
          AND (t.audio_features ->> 'Danceability')     IS NOT NULL
          AND (t.audio_features ->> 'Energy')           IS NOT NULL
          AND (t.audio_features ->> 'Valence')          IS NOT NULL
          AND (t.audio_features ->> 'Tempo')            IS NOT NULL
          AND (t.audio_features ->> 'Acousticness')     IS NOT NULL
          AND (t.audio_features ->> 'Instrumentalness') IS NOT NULL
          AND (t.audio_features ->> 'Liveness')         IS NOT NULL
          AND (t.audio_features ->> 'Speechiness')      IS NOT NULL
          AND (t.audio_features ->> 'Loudness')         IS NOT NULL
        ORDER BY t.id
        LIMIT @BatchSize;
        """;

    public CatalogSimilarityFeatureSource(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RawTrackFeatures> StreamEligibleTracksAsync(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int effectiveBatchSize = Math.Clamp(batchSize, MinimumBatchSize, MaximumBatchSize);

        using IDbConnection connection = await OpenConnectionAsync();

        // A string vazia ordena antes de qualquer id, então é a âncora inicial do keyset sem um parâmetro nulo.
        string afterId = string.Empty;

        while (!cancellationToken.IsCancellationRequested)
        {
            var command = new CommandDefinition(
                Sql,
                new { AfterId = afterId, BatchSize = effectiveBatchSize },
                cancellationToken: cancellationToken);

            EligibleTrackRow[] batch = (await connection.QueryAsync<EligibleTrackRow>(command)).ToArray();

            if (batch.Length == 0)
                yield break;

            foreach (EligibleTrackRow row in batch)
                yield return row.ToRawFeatures();

            afterId = batch[^1].TrackId;

            if (batch.Length < effectiveBatchSize)
                yield break;
        }
    }

    /// <summary>
    /// Projeção crua de uma faixa elegível. As nove features não são anuláveis aqui porque o <c>WHERE</c> já as
    /// exigiu presentes — mapeá-las como <c>double</c> torna esse contrato explícito e faz um jsonb incompleto
    /// (que não deveria escapar do filtro) falhar alto na materialização, em vez de virar zero disfarçado.
    /// </summary>
    private sealed record EligibleTrackRow(
        string TrackId,
        bool IsImputed,
        double Danceability,
        double Energy,
        double Valence,
        double Tempo,
        double Acousticness,
        double Instrumentalness,
        double Liveness,
        double Speechiness,
        double Loudness)
    {
        /// <summary>Monta o vetor CRU na ORDEM CANÔNICA de <see cref="SimilarityFeatures.Ordered"/>.</summary>
        public RawTrackFeatures ToRawFeatures()
        {
            SimilarityFeatureVector rawVector = SimilarityFeatureVector.Create(
            [
                Danceability,
                Energy,
                Valence,
                Tempo,
                Acousticness,
                Instrumentalness,
                Liveness,
                Speechiness,
                Loudness
            ]);

            return new RawTrackFeatures(TrackId, rawVector, IsImputed);
        }
    }
}
