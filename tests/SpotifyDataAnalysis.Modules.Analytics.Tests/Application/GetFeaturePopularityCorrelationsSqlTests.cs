using SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

namespace SpotifyDataAnalysis.Modules.Analytics.Tests.Application;

/// <summary>
/// Contrato do SQL das correlações, afirmado sobre o texto da query (sem Postgres vivo). O valor do coeficiente
/// em si só um banco real calcula — isso é coberto pelo smoke test com amostra de correlação conhecida.
/// </summary>
public sealed class GetFeaturePopularityCorrelationsSqlTests
{
    private static readonly string Sql = GetFeaturePopularityCorrelationsQueryHandler.Sql;

    private static readonly AudioFeatureKind[] ContinuousFeatures =
    [
        AudioFeatureKind.Danceability,
        AudioFeatureKind.Energy,
        AudioFeatureKind.Valence,
        AudioFeatureKind.Tempo,
        AudioFeatureKind.Acousticness,
        AudioFeatureKind.Instrumentalness,
        AudioFeatureKind.Liveness,
        AudioFeatureKind.Speechiness,
        AudioFeatureKind.Loudness
    ];

    [Fact]
    public void Sql_ComputesPearsonInTheDatabase_NotInTheApplication()
    {
        Assert.Contains("corr(popularity, value)", Sql);
    }

    [Theory]
    [InlineData(AudioFeatureKind.Danceability)]
    [InlineData(AudioFeatureKind.Energy)]
    [InlineData(AudioFeatureKind.Valence)]
    [InlineData(AudioFeatureKind.Tempo)]
    [InlineData(AudioFeatureKind.Acousticness)]
    [InlineData(AudioFeatureKind.Instrumentalness)]
    [InlineData(AudioFeatureKind.Liveness)]
    [InlineData(AudioFeatureKind.Speechiness)]
    [InlineData(AudioFeatureKind.Loudness)]
    public void Sql_WhitelistsEveryContinuousFeature_AsALiteralInTheQuery(AudioFeatureKind feature)
    {
        Assert.Contains($"('{AudioFeatureJsonKey.Of(feature)}')", Sql);
    }

    [Theory]
    [InlineData(AudioFeatureKind.Key)]
    [InlineData(AudioFeatureKind.Mode)]
    [InlineData(AudioFeatureKind.TimeSignature)]
    public void Sql_ExcludesDiscreteFeatures_WherePearsonWouldBeMeaningless(AudioFeatureKind feature)
    {
        Assert.DoesNotContain($"('{AudioFeatureJsonKey.Of(feature)}')", Sql);
    }

    [Fact]
    public void Sql_WhitelistIsExactlyTheNineContinuousFeatures_NoUserSuppliedColumnName()
    {
        // A lista de features é literal no SQL, então não existe nome de coluna vindo do usuário. O @ só
        // aparece nos parâmetros de recorte e paginação — nunca compondo um identificador.
        int literalCount = ContinuousFeatures
            .Count(feature => Sql.Contains($"('{AudioFeatureJsonKey.Of(feature)}')", StringComparison.Ordinal));

        Assert.Equal(9, literalCount);
        Assert.DoesNotContain("@Feature", Sql);
    }

    [Fact]
    public void Sql_ReadsFeatureValueFromJsonb_ByTheWhitelistedKey()
    {
        Assert.Contains("(c.audio_features ->> f.feature)::double precision", Sql);
    }

    [Fact]
    public void Sql_OnlyConsidersTracksThatHaveAudioFeatures()
    {
        Assert.Contains("WHERE t.audio_features IS NOT NULL", Sql);
    }

    [Fact]
    public void Sql_ImputedCut_IsOptional_AndExcludesByDefault()
    {
        Assert.Contains("WHERE @IncludeImputed OR NOT is_imputed", Sql);
        Assert.Contains("COALESCE((t.audio_features ->> 'IsImputed')::boolean, false)", Sql);
    }

    [Fact]
    public void Sql_DropsPairsWithoutAValue_SoNIsTheRealSampleSize()
    {
        // corr() já ignora NULL, mas o COUNT(value) tem de contar o mesmo conjunto para o N reportado não
        // mentir sobre o tamanho da amostra usada no coeficiente.
        Assert.Contains("WHERE value IS NOT NULL", Sql);
        Assert.Contains("COUNT(value)", Sql);
    }

    [Fact]
    public void Sql_ReturnsMetadataInItsOwnResultSet_SoItSurvivesAnEmptySample()
    {
        Assert.Contains("""AS "MeasuredCount",""", Sql);
        Assert.Contains("""AS "ImputedCount",""", Sql);
        Assert.Contains("""AS "ConsideredCount";""", Sql);

        Assert.Equal(2, Sql.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Count(fragment => fragment.Contains("SELECT", StringComparison.Ordinal)));
    }

    [Fact]
    public void Sql_AppliesTheSamePopulationCut_ToMetadataAndToCoefficients()
    {
        const string populationCut = "WHERE @IncludeImputed OR NOT is_imputed";

        Assert.Equal(2, Sql.Split(populationCut).Length - 1);
    }

    [Fact]
    public void Sql_MeasuredAndImputedCounts_ComeFromTheWholeCandidatePopulation()
    {
        Assert.Contains("(SELECT COUNT(*) FROM candidates WHERE NOT is_imputed)", Sql);
        Assert.Contains("(SELECT COUNT(*) FROM candidates WHERE is_imputed)", Sql);
    }

    [Fact]
    public void Sql_FollowsPostgresConventions_NoSqlServerDialect()
    {
        Assert.DoesNotContain("NOLOCK", Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[", Sql);
        Assert.DoesNotContain("TOP ", Sql, StringComparison.OrdinalIgnoreCase);
    }
}
