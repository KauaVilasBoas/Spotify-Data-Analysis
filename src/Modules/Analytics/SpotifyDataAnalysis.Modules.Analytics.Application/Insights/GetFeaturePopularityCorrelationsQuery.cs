using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

/// <summary>
/// Correlação de Pearson entre cada audio-feature contínua e a popularidade da faixa, calculada no banco com
/// <c>corr()</c>. Entrega o vetor feature×popularidade, não a matriz feature×feature.
/// </summary>
/// <remarks>
/// Só as 9 features CONTÍNUAS entram. <c>Key</c>, <c>Mode</c> e <c>TimeSignature</c> ficam de fora porque
/// Pearson pressupõe relação linear entre grandezas contínuas: <c>Key</c> é classe de altura cíclica (11 e 0
/// são vizinhos), <c>Mode</c> é binário e <c>TimeSignature</c> é discreto — um coeficiente sobre eles seria um
/// número sem significado. Pela mesma razão, o coeficiente de <c>Loudness</c> (dB) e <c>Tempo</c> (BPM) mede
/// só a componente linear da relação.
/// </remarks>
public sealed record GetFeaturePopularityCorrelationsQuery : IQuery<FeaturePopularityCorrelationsResult>
{
    /// <summary>Se as faixas com features imputadas entram no cálculo.</summary>
    public bool IncludeImputed { get; init; }
}

/// <summary>
/// O coeficiente de uma feature contra a popularidade. <paramref name="Coefficient"/> é nulo quando o
/// PostgreSQL não consegue calculá-lo — menos de dois pares ou variância zero na amostra.
/// </summary>
public sealed record FeaturePopularityCorrelation(
    AudioFeatureKind Feature,
    double? Coefficient,
    long N);

/// <summary>
/// Os coeficientes e o contexto da amostra. <c>MeasuredCount</c> e <c>ImputedCount</c> vêm preenchidos mesmo
/// quando as imputadas foram excluídas, e <c>IncludedImputed</c> ecoa o recorte aplicado. O <c>N</c> por feature
/// pode ser menor que <c>ConsideredCount</c> se uma chave estiver ausente no jsonb de alguma faixa.
/// </summary>
public sealed record FeaturePopularityCorrelationsResult(
    IReadOnlyList<FeaturePopularityCorrelation> Correlations,
    long ConsideredCount,
    long MeasuredCount,
    long ImputedCount,
    bool IncludedImputed);

internal sealed class GetFeaturePopularityCorrelationsQueryHandler
    : BaseDataAccess, IQueryHandler<GetFeaturePopularityCorrelationsQuery, FeaturePopularityCorrelationsResult>
{
    private const string PopulationCte =
        """
        candidates AS (
            SELECT
                t.popularity,
                t.audio_features,
                COALESCE((t.audio_features ->> 'IsImputed')::boolean, false) AS is_imputed
            FROM catalog.tracks AS t
            WHERE t.audio_features IS NOT NULL
        ),
        considered AS (
            SELECT popularity, audio_features
            FROM candidates
            WHERE @IncludeImputed OR NOT is_imputed
        )
        """;

    internal const string Sql =
        $"""
        WITH {PopulationCte}
        SELECT
            (SELECT COUNT(*) FROM candidates WHERE NOT is_imputed)  AS "MeasuredCount",
            (SELECT COUNT(*) FROM candidates WHERE is_imputed)      AS "ImputedCount",
            (SELECT COUNT(*) FROM considered)                       AS "ConsideredCount";

        WITH {PopulationCte},
        observations AS (
            SELECT
                f.feature                                          AS feature,
                c.popularity::double precision                     AS popularity,
                (c.audio_features ->> f.feature)::double precision AS value
            FROM considered AS c
            CROSS JOIN (VALUES
                ('Danceability'),
                ('Energy'),
                ('Valence'),
                ('Tempo'),
                ('Acousticness'),
                ('Instrumentalness'),
                ('Liveness'),
                ('Speechiness'),
                ('Loudness')
            ) AS f(feature)
        )
        SELECT
            feature                     AS "Feature",
            corr(popularity, value)     AS "Coefficient",
            COUNT(value)                AS "N"
        FROM observations
        WHERE value IS NOT NULL
        GROUP BY feature
        ORDER BY feature;
        """;

    public GetFeaturePopularityCorrelationsQueryHandler(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<FeaturePopularityCorrelationsResult> HandleAsync(
        GetFeaturePopularityCorrelationsQuery request, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        var command = new CommandDefinition(
            Sql, new { request.IncludeImputed }, cancellationToken: cancellationToken);

        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        MetadataRow metadata = await grid.ReadSingleAsync<MetadataRow>();

        IReadOnlyList<FeaturePopularityCorrelation> correlations =
            (await grid.ReadAsync<CorrelationRow>())
            .Select(row => new FeaturePopularityCorrelation(
                Enum.Parse<AudioFeatureKind>(row.Feature), row.Coefficient, row.N))
            .ToArray();

        return new FeaturePopularityCorrelationsResult(
            correlations,
            metadata.ConsideredCount,
            metadata.MeasuredCount,
            metadata.ImputedCount,
            request.IncludeImputed);
    }

    private sealed record MetadataRow(long MeasuredCount, long ImputedCount, long ConsideredCount);

    private sealed record CorrelationRow(string Feature, double? Coefficient, long N);
}
