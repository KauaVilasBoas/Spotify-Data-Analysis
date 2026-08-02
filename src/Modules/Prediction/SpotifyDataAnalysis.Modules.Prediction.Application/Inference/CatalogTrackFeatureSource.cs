using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Inference;

/// <summary>
/// Leitura pontual das features de uma faixa do schema <c>catalog</c> com Dapper, herdando
/// <see cref="BaseDataAccess"/>. Acoplamento por DADO idêntico ao do E3.1: lê a tabela do Catalog por SQL, sem
/// referenciar nenhum tipo .NET daquele módulo — posição guardada pelos ArchTests.
///
/// <para>O SELECT das audio-features é o MESMO do <c>CatalogTrackTrainingCandidateSource</c> (E3.1),
/// deliberadamente: montar o insumo da inferência por uma projeção diferente da do treino seria abrir a porta
/// do skew justamente na leitura. As chaves do jsonb são PascalCase porque são os nomes das propriedades CLR do
/// value object do Catalog serializado como JSON — a convenção snake_case do DbContext não alcança owned types
/// em JSON.</para>
/// </summary>
internal sealed class CatalogTrackFeatureSource : BaseDataAccess, ITrackFeatureSource
{
    /// <summary>
    /// Projeção de UMA faixa por id. Diferente do E3.1, aqui há <c>WHERE id = @TrackId</c> — é busca pontual,
    /// não varredura paginada. Sem popularidade na projeção: prever não precisa do alvo real (o smoke test o lê
    /// à parte para comparar previsto × real).
    /// </summary>
    internal const string Sql =
        """
        SELECT
            t.id                                                            AS "TrackId",
            (t.audio_features IS NOT NULL)                                  AS "HasAudioFeatures",
            COALESCE((t.audio_features ->> 'IsImputed')::boolean, false)    AS "IsImputed",
            t.duration_ms                                                   AS "DurationMs",
            t.explicit                                                      AS "Explicit",
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
        WHERE t.id = @TrackId;
        """;

    public CatalogTrackFeatureSource(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    /// <inheritdoc />
    public async Task<TrackFeatureRow?> FindByTrackIdAsync(
        string trackId, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        var command = new CommandDefinition(
            Sql, new { TrackId = trackId }, cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<TrackFeatureRow>(command);
    }
}
