using System.Data;
using Dapper;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

/// <summary>
/// Distribuição (histograma) de uma audio-feature: quantas faixas caem em cada faixa de valor. Os buckets têm
/// largura fixa entre o mínimo e o máximo observados, o que faz a mesma query servir features 0–1, loudness em
/// dB negativo e tempo em BPM. Faixas com features imputadas ficam fora por padrão.
/// </summary>
public sealed record GetAudioFeatureDistributionQuery : IQuery<AudioFeatureDistributionResult>
{
    public const int DefaultBuckets = 20;
    public const int MaxBuckets = 200;

    private readonly int _buckets = DefaultBuckets;

    /// <summary>A audio-feature a distribuir.</summary>
    public required AudioFeatureKind Feature { get; init; }

    /// <summary>Quantidade de buckets do histograma, presa a [1, <see cref="MaxBuckets"/>].</summary>
    public int Buckets
    {
        get => _buckets;
        init => _buckets = Math.Clamp(value, 1, MaxBuckets);
    }

    /// <summary>Se as faixas com features imputadas entram no histograma.</summary>
    public bool IncludeImputed { get; init; }
}

/// <summary>
/// Um bucket do histograma. <paramref name="UpperBound"/> é exclusivo, exceto no último bucket, que inclui o
/// máximo observado.
/// </summary>
public sealed record AudioFeatureDistributionBucket(
    int Index,
    double LowerBound,
    double UpperBound,
    long Count);

/// <summary>
/// A distribuição da feature e os metadados do recorte. <c>MeasuredCount</c> e <c>ImputedCount</c> vêm
/// preenchidos mesmo quando as imputadas foram excluídas, e <c>IncludedImputed</c> ecoa o recorte aplicado, de
/// modo que as duas populações nunca se misturem sem sinalização. Buckets vazios do meio vêm com contagem zero;
/// a lista é vazia quando nada foi considerado.
/// </summary>
public sealed record AudioFeatureDistributionResult(
    AudioFeatureKind Feature,
    IReadOnlyList<AudioFeatureDistributionBucket> Buckets,
    long TotalConsidered,
    long MeasuredCount,
    long ImputedCount,
    bool IncludedImputed,
    double? MinValue,
    double? MaxValue);

internal sealed class GetAudioFeatureDistributionQueryHandler
    : BaseDataAccess, IQueryHandler<GetAudioFeatureDistributionQuery, AudioFeatureDistributionResult>
{
    private const string PopulationCte =
        """
        candidates AS (
            SELECT
                (t.audio_features ->> @FeatureKey)::double precision           AS value,
                COALESCE((t.audio_features ->> 'IsImputed')::boolean, false)   AS is_imputed
            FROM catalog.tracks AS t
            WHERE t.audio_features IS NOT NULL
              AND t.audio_features ->> @FeatureKey IS NOT NULL
        ),
        considered AS (
            SELECT value
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
            (SELECT COUNT(*) FROM considered)                       AS "TotalConsidered",
            (SELECT MIN(value) FROM considered)                     AS "MinValue",
            (SELECT MAX(value) FROM considered)                     AS "MaxValue";

        WITH {PopulationCte},
        bounds AS (
            SELECT MIN(value) AS min_value, MAX(value) AS max_value, COUNT(*) AS total
            FROM considered
        ),
        layout AS (
            SELECT
                min_value,
                max_value,
                total,
                CASE WHEN max_value > min_value THEN @Buckets ELSE 1 END              AS bucket_count,
                CASE WHEN max_value > min_value THEN max_value ELSE min_value + 1 END AS upper_bound
            FROM bounds
        ),
        sized AS (
            SELECT *, (upper_bound - min_value) / bucket_count AS width
            FROM layout
        ),
        tallies AS (
            SELECT
                LEAST(
                    width_bucket(c.value, s.min_value, s.upper_bound, s.bucket_count),
                    s.bucket_count
                )        AS bucket_index,
                COUNT(*) AS tally
            FROM considered AS c
            CROSS JOIN sized AS s
            GROUP BY 1
        )
        SELECT
            g.bucket_index                                              AS "Index",
            s.min_value + (g.bucket_index - 1) * s.width                AS "LowerBound",
            LEAST(s.min_value + g.bucket_index * s.width, s.max_value)  AS "UpperBound",
            COALESCE(t.tally, 0)                                        AS "Count"
        FROM sized AS s
        CROSS JOIN generate_series(1, s.bucket_count) AS g(bucket_index)
        LEFT JOIN tallies AS t ON t.bucket_index = g.bucket_index
        WHERE s.total > 0
        ORDER BY g.bucket_index;
        """;

    public GetAudioFeatureDistributionQueryHandler(DbConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<AudioFeatureDistributionResult> HandleAsync(
        GetAudioFeatureDistributionQuery request, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = await OpenConnectionAsync();

        var command = new CommandDefinition(
            Sql,
            new
            {
                FeatureKey = AudioFeatureJsonKey.Of(request.Feature),
                request.IncludeImputed,
                request.Buckets
            },
            cancellationToken: cancellationToken);

        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        MetadataRow metadata = await grid.ReadSingleAsync<MetadataRow>();

        IReadOnlyList<AudioFeatureDistributionBucket> buckets =
            (await grid.ReadAsync<AudioFeatureDistributionBucket>()).AsList();

        return new AudioFeatureDistributionResult(
            request.Feature,
            buckets,
            metadata.TotalConsidered,
            metadata.MeasuredCount,
            metadata.ImputedCount,
            request.IncludeImputed,
            metadata.MinValue,
            metadata.MaxValue);
    }

    private sealed record MetadataRow(
        long MeasuredCount,
        long ImputedCount,
        long TotalConsidered,
        double? MinValue,
        double? MaxValue);
}
