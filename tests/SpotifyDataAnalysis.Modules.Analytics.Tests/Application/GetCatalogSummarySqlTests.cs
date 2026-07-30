using SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

namespace SpotifyDataAnalysis.Modules.Analytics.Tests.Application;

/// <summary>
/// Testes do CONTRATO do SQL do resumo do catálogo (E2.1). Sem Postgres vivo: afirmam, sobre o texto do
/// SQL, as invariantes que os critérios de aceite exigem — o recorte medido vs imputado por
/// <c>audio_features -&gt;&gt; 'IsImputed'</c> e a aderência ao dialeto PostgreSQL do <c>BaseDataAccess</c>.
/// A execução real contra o schema fica para o smoke test com banco (Docker), sinalizado como posterior.
/// </summary>
public sealed class GetCatalogSummarySqlTests
{
    private static readonly string Sql = GetCatalogSummaryQueryHandler.Sql;

    [Fact]
    public void Sql_ReadsImputedFlag_FromAudioFeaturesJsonb()
    {
        // O flag imputado mora DENTRO do jsonb; extraí-lo por "->> 'IsImputed'" é o coração do recorte.
        Assert.Contains("audio_features ->> 'IsImputed'", Sql);
    }

    [Fact]
    public void Sql_MeasuredCount_ExcludesImputed_NeverCountingImputedAsMeasured()
    {
        // Medido = feature presente E não imputada. A cláusula que produz "TracksWithMeasuredFeatures" tem de
        // filtrar IsImputed = false; sem isso, imputados vazariam para a contagem de medidos.
        Assert.Contains(
            "COALESCE((audio_features ->> 'IsImputed')::boolean, false) = false)    AS \"TracksWithMeasuredFeatures\"",
            Sql);
    }

    [Fact]
    public void Sql_ImputedCount_IsReportedSeparately()
    {
        Assert.Contains(
            "COALESCE((audio_features ->> 'IsImputed')::boolean, false) = true)     AS \"TracksWithImputedFeatures\"",
            Sql);
    }

    [Fact]
    public void Sql_WithAndWithoutFeatures_SplitOnJsonbNullability()
    {
        Assert.Contains("audio_features IS NOT NULL)                                            AS \"TracksWithAudioFeatures\"", Sql);
        Assert.Contains("audio_features IS NULL)                                                AS \"TracksWithoutAudioFeatures\"", Sql);
    }

    [Fact]
    public void Sql_DistinctGenres_ComeFromAudioFeaturesJsonb_ExcludingNull()
    {
        Assert.Contains("COUNT(DISTINCT audio_features ->> 'Genre')", Sql);
        Assert.Contains("audio_features ->> 'Genre' IS NOT NULL", Sql);
    }

    [Fact]
    public void Sql_CountsArtistsAndAlbums_FromTheirOwnCatalogTables()
    {
        Assert.Contains("FROM catalog.artists", Sql);
        Assert.Contains("FROM catalog.albums", Sql);
    }

    [Fact]
    public void Sql_FollowsPostgresConventions_NoSqlServerDialect()
    {
        // Convenção do BaseDataAccess: dialeto PostgreSQL, não SQL Server.
        Assert.DoesNotContain("WITH(NOLOCK)", Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NOLOCK", Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[", Sql); // sem identificadores entre colchetes (T-SQL)
    }
}
