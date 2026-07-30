using System.Data;
using System.Runtime.CompilerServices;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Training;

/// <summary>
/// Leitura das faixas candidatas direto do schema <c>catalog</c> com Dapper, herdando
/// <see cref="BaseDataAccess"/>. Acoplamento por DADO: o Prediction lê a tabela do Catalog por SQL e não
/// referencia nenhum tipo .NET daquele módulo — é a mesma posição do Analytics, guardada pelos ArchTests.
///
/// <para><b>Paginação por keyset</b> (<c>id &gt; @AfterId ORDER BY id LIMIT @BatchSize</c>) e não por
/// <c>OFFSET</c>: o keyset usa a chave primária como índice de partida, mantém custo constante por lote
/// (<c>OFFSET</c> reler-e-descarta, ficando quadrático ao varrer a tabela inteira) e é estável contra
/// inserções concorrentes. O lote também é o teto do pico de memória do transporte, que é o que permite
/// montar o dataset num host de free tier.</para>
///
/// <para>As audio-features vivem numa coluna <c>jsonb</c> cujas CHAVES são os nomes das propriedades CLR do
/// value object do Catalog — daí o PascalCase (a convenção snake_case do DbContext não alcança owned types
/// serializados como JSON). Faixa sem features tem <c>audio_features</c> NULL, e uma chave ausente vira NULL
/// na projeção: os dois casos chegam ao domínio como estado, não como zero disfarçado.</para>
/// </summary>
internal sealed class CatalogTrackTrainingCandidateSource : BaseDataAccess, ITrackTrainingCandidateSource
{
    /// <summary>Menor lote aceito, para o keyset não degenerar em uma ida ao banco por faixa.</summary>
    internal const int MinimumBatchSize = 100;

    /// <summary>Maior lote aceito, para o result set de um lote não virar o problema que a paginação evita.</summary>
    internal const int MaximumBatchSize = 50_000;

    /// <summary>
    /// Projeção crua das faixas. É deliberadamente burra: nenhum <c>WHERE</c> de elegibilidade, porque quem
    /// decide quem entra no dataset é a specification do domínio — o SQL só entrega o que existe.
    /// </summary>
    internal const string Sql =
        """
        SELECT
            t.id                                                            AS "TrackId",
            t.popularity                                                    AS "Popularity",
            (t.audio_features IS NOT NULL)                                  AS "HasAudioFeatures",
            COALESCE((t.audio_features ->> 'IsImputed')::boolean, false)    AS "IsImputed",
            (t.audio_features ->> 'Danceability')::double precision         AS "Danceability",
            (t.audio_features ->> 'Energy')::double precision               AS "Energy",
            (t.audio_features ->> 'Valence')::double precision              AS "Valence",
            (t.audio_features ->> 'Tempo')::double precision                AS "Tempo",
            (t.audio_features ->> 'Acousticness')::double precision         AS "Acousticness",
            (t.audio_features ->> 'Instrumentalness')::double precision     AS "Instrumentalness",
            (t.audio_features ->> 'Liveness')::double precision             AS "Liveness",
            (t.audio_features ->> 'Speechiness')::double precision          AS "Speechiness",
            (t.audio_features ->> 'Loudness')::double precision             AS "Loudness",
            (t.audio_features ->> 'Key')::integer                           AS "Key",
            (t.audio_features ->> 'Mode')::integer                          AS "Mode",
            (t.audio_features ->> 'TimeSignature')::integer                 AS "TimeSignature",
            (t.audio_features ->> 'Genre')                                  AS "Genre"
        FROM catalog.tracks AS t
        WHERE t.id > @AfterId
        ORDER BY t.id
        LIMIT @BatchSize;
        """;

    public CatalogTrackTrainingCandidateSource(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TrackTrainingCandidate> StreamCandidatesAsync(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int effectiveBatchSize = Math.Clamp(batchSize, MinimumBatchSize, MaximumBatchSize);

        using IDbConnection connection = await OpenConnectionAsync();

        // A string vazia ordena antes de qualquer id, então serve de âncora inicial do keyset sem um
        // parâmetro nulo (que exigiria tipagem explícita no Npgsql).
        string afterId = string.Empty;

        while (!cancellationToken.IsCancellationRequested)
        {
            var command = new CommandDefinition(
                Sql,
                new { AfterId = afterId, BatchSize = effectiveBatchSize },
                cancellationToken: cancellationToken);

            CandidateRow[] batch = (await connection.QueryAsync<CandidateRow>(command)).ToArray();

            if (batch.Length == 0)
                yield break;

            foreach (CandidateRow row in batch)
            {
                yield return row.ToCandidate();
            }

            afterId = batch[^1].TrackId;

            if (batch.Length < effectiveBatchSize)
                yield break;
        }
    }

    private sealed record CandidateRow(
        string TrackId,
        int? Popularity,
        bool HasAudioFeatures,
        bool IsImputed,
        double? Danceability,
        double? Energy,
        double? Valence,
        double? Tempo,
        double? Acousticness,
        double? Instrumentalness,
        double? Liveness,
        double? Speechiness,
        double? Loudness,
        int? Key,
        int? Mode,
        int? TimeSignature,
        string? Genre)
    {
        public TrackTrainingCandidate ToCandidate() => new()
        {
            TrackId = TrackId,
            Popularity = Popularity,
            HasAudioFeatures = HasAudioFeatures,
            IsImputed = IsImputed,
            Danceability = Danceability,
            Energy = Energy,
            Valence = Valence,
            Tempo = Tempo,
            Acousticness = Acousticness,
            Instrumentalness = Instrumentalness,
            Liveness = Liveness,
            Speechiness = Speechiness,
            Loudness = Loudness,
            Key = Key,
            Mode = Mode,
            TimeSignature = TimeSignature,
            Genre = Genre
        };
    }
}
