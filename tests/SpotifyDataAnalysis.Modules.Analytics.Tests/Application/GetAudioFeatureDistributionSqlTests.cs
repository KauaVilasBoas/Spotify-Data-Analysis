using SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

namespace SpotifyDataAnalysis.Modules.Analytics.Tests.Application;

/// <summary>
/// Contrato do SQL da distribuição de audio-feature, afirmado sobre o texto da query (sem Postgres vivo).
/// </summary>
public sealed class GetAudioFeatureDistributionSqlTests
{
    private static readonly string Sql = GetAudioFeatureDistributionQueryHandler.Sql;

    [Fact]
    public void Sql_ReadsFeatureValue_ThroughAParameterizedJsonbKey_NotAConcatenatedIdentifier()
    {
        Assert.Contains("(t.audio_features ->> @FeatureKey)::double precision", Sql);
        Assert.Contains("t.audio_features ->> @FeatureKey IS NOT NULL", Sql);
    }

    [Fact]
    public void Sql_ExcludesTracksWithoutAudioFeatures()
    {
        Assert.Contains("t.audio_features IS NOT NULL", Sql);
    }

    [Fact]
    public void Sql_ImputedCut_IsOptional_AndReadsIsImputedFromJsonb()
    {
        Assert.Contains("COALESCE((t.audio_features ->> 'IsImputed')::boolean, false)", Sql);
        Assert.Contains("WHERE @IncludeImputed OR NOT is_imputed", Sql);
    }

    [Fact]
    public void Sql_BucketsByWidthBucket_OverObservedMinAndMax()
    {
        Assert.Contains("width_bucket(c.value, s.min_value, s.upper_bound, s.bucket_count)", Sql);
        Assert.Contains("SELECT MIN(value) AS min_value, MAX(value) AS max_value", Sql);
    }

    [Fact]
    public void Sql_FoldsTheMaximumValueIntoTheLastBucket_NoPhantomOverflowBucket()
    {
        string normalized = string.Join(' ', Sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains(
            "LEAST( width_bucket(c.value, s.min_value, s.upper_bound, s.bucket_count), s.bucket_count )",
            normalized);
    }

    [Fact]
    public void Sql_GuardsTheDegenerateCase_WhereEveryConsideredValueIsEqual()
    {
        Assert.Contains("CASE WHEN max_value > min_value THEN @Buckets ELSE 1 END", Sql);
        Assert.Contains("CASE WHEN max_value > min_value THEN max_value ELSE min_value + 1 END", Sql);
    }

    [Fact]
    public void Sql_MaterializesEveryBucket_SoEmptyInteriorBucketsComeBackWithZero()
    {
        Assert.Contains("CROSS JOIN generate_series(1, s.bucket_count) AS g(bucket_index)", Sql);
        Assert.Contains("LEFT JOIN tallies AS t ON t.bucket_index = g.bucket_index", Sql);
        Assert.Contains("COALESCE(t.tally, 0)", Sql);
    }

    [Fact]
    public void Sql_ReportsUpperBoundCappedAtTheObservedMaximum()
    {
        Assert.Contains("LEAST(s.min_value + g.bucket_index * s.width, s.max_value)", Sql);
    }

    [Fact]
    public void Sql_DropsThePhantomBucket_WhenNothingWasConsidered()
    {
        Assert.Contains("WHERE s.total > 0", Sql);
    }

    [Fact]
    public void Sql_ReturnsMetadataInItsOwnResultSet_SoItSurvivesAnEmptyHistogram()
    {
        Assert.Contains("""AS "MeasuredCount",""", Sql);
        Assert.Contains("""AS "ImputedCount",""", Sql);
        Assert.Contains("""AS "TotalConsidered",""", Sql);
        Assert.Contains("""AS "MinValue",""", Sql);
        Assert.Contains("""AS "MaxValue";""", Sql);

        Assert.Equal(2, Sql.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Count(fragment => fragment.Contains("SELECT", StringComparison.Ordinal)));
    }

    [Fact]
    public void Sql_AppliesTheSamePopulationCut_ToMetadataAndToBuckets()
    {
        const string populationCut = "WHERE @IncludeImputed OR NOT is_imputed";

        int occurrences = Sql.Split(populationCut).Length - 1;
        Assert.Equal(2, occurrences);
    }

    [Fact]
    public void Sql_MeasuredAndImputedCounts_AreTakenFromTheWholeCandidatePopulation_NotTheConsideredCut()
    {
        Assert.Contains("(SELECT COUNT(*) FROM candidates WHERE NOT is_imputed)", Sql);
        Assert.Contains("(SELECT COUNT(*) FROM candidates WHERE is_imputed)", Sql);
        Assert.Contains("(SELECT COUNT(*) FROM considered)", Sql);
    }

    [Fact]
    public void Sql_FollowsPostgresConventions_NoSqlServerDialect()
    {
        Assert.DoesNotContain("WITH(NOLOCK)", Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NOLOCK", Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[", Sql);
        Assert.DoesNotContain("TOP ", Sql, StringComparison.OrdinalIgnoreCase);
    }
}
