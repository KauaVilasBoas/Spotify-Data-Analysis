using SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

namespace SpotifyDataAnalysis.Modules.Analytics.Tests.Application;

/// <summary>
/// Contrato do SQL dos recortes agregados (gênero, artista, álbum e ano), afirmado sobre o texto das queries.
/// </summary>
public sealed class AggregateInsightsSqlTests
{
    private static readonly string GenreSql = GetGenreInsightsQueryHandler.Sql;
    private static readonly string ArtistSql = GetArtistInsightsQueryHandler.Sql;
    private static readonly string AlbumSql = GetAlbumInsightsQueryHandler.Sql;
    private static readonly string YearSql = GetAlbumYearInsightsQueryHandler.Sql;

    // ---- Deriva entre a chave de ordenação do C# e a do SQL ----
    //
    // É o teste mais importante deste arquivo: se o mapeamento em C# devolver uma chave que o CASE do SQL não
    // reconhece, nenhum ramo casa, a ordenação silenciosamente degrada para o desempate e o endpoint "funciona"
    // devolvendo a ordem errada. Compila, passa em teste de controller e ninguém percebe.

    [Theory]
    [InlineData(GenreInsightSort.AveragePopularityDesc)]
    [InlineData(GenreInsightSort.TrackCountDesc)]
    [InlineData(GenreInsightSort.GenreAsc)]
    public void GenreSortKey_IsRecognizedByTheSql(GenreInsightSort sort)
    {
        Assert.Contains($"@SortBy = '{GetGenreInsightsQueryHandler.SortKeyOf(sort)}'", GenreSql);
    }

    [Theory]
    [InlineData(ArtistInsightSort.PopularityDesc)]
    [InlineData(ArtistInsightSort.FollowersDesc)]
    [InlineData(ArtistInsightSort.NameAsc)]
    public void ArtistSortKey_IsRecognizedByTheSql(ArtistInsightSort sort)
    {
        Assert.Contains($"@SortBy = '{GetArtistInsightsQueryHandler.SortKeyOf(sort)}'", ArtistSql);
    }

    [Theory]
    [InlineData(AlbumInsightSort.AveragePopularityDesc)]
    [InlineData(AlbumInsightSort.TrackCountDesc)]
    [InlineData(AlbumInsightSort.NameAsc)]
    public void AlbumSortKey_IsRecognizedByTheSql(AlbumInsightSort sort)
    {
        Assert.Contains($"@SortBy = '{GetAlbumInsightsQueryHandler.SortKeyOf(sort)}'", AlbumSql);
    }

    [Theory]
    [InlineData(AlbumYearInsightSort.YearDesc)]
    [InlineData(AlbumYearInsightSort.YearAsc)]
    [InlineData(AlbumYearInsightSort.AveragePopularityDesc)]
    public void YearSortKey_IsRecognizedByTheSql(AlbumYearInsightSort sort)
    {
        Assert.Contains($"@SortBy = '{GetAlbumYearInsightsQueryHandler.SortKeyOf(sort)}'", YearSql);
    }

    [Fact]
    public void EverySortEnumMember_HasAMappedKey()
    {
        foreach (GenreInsightSort sort in Enum.GetValues<GenreInsightSort>())
            Assert.False(string.IsNullOrWhiteSpace(GetGenreInsightsQueryHandler.SortKeyOf(sort)));

        foreach (ArtistInsightSort sort in Enum.GetValues<ArtistInsightSort>())
            Assert.False(string.IsNullOrWhiteSpace(GetArtistInsightsQueryHandler.SortKeyOf(sort)));

        foreach (AlbumInsightSort sort in Enum.GetValues<AlbumInsightSort>())
            Assert.False(string.IsNullOrWhiteSpace(GetAlbumInsightsQueryHandler.SortKeyOf(sort)));

        foreach (AlbumYearInsightSort sort in Enum.GetValues<AlbumYearInsightSort>())
            Assert.False(string.IsNullOrWhiteSpace(GetAlbumYearInsightsQueryHandler.SortKeyOf(sort)));
    }

    // ---- Gênero ----

    [Fact]
    public void GenreSql_BucketsTracksWithoutGenre_InsteadOfDroppingThem()
    {
        Assert.Contains("COALESCE(t.audio_features ->> 'Genre', @NoGenreLabel)", GenreSql);
        Assert.Equal("(sem gênero)", GetGenreInsightsQueryHandler.NoGenreLabel);
    }

    [Fact]
    public void GenreSql_HasNoWhereClause_SoEveryTrackIsAccountedFor()
    {
        // A soma das contagens tem de fechar com o total do catálogo: qualquer filtro aqui faria faixas
        // desaparecerem do recorte sem aviso.
        Assert.DoesNotContain("WHERE t.", GenreSql);
    }

    [Fact]
    public void GenreSql_AggregatesAverageAndMedianAndCounts()
    {
        Assert.Contains("AVG(t.popularity)::double precision", GenreSql);
        Assert.Contains("percentile_cont(0.5) WITHIN GROUP (ORDER BY t.popularity)", GenreSql);
        Assert.Contains("COUNT(*)", GenreSql);
    }

    [Fact]
    public void GenreSql_ReportsHowManyTracksInTheBucketHaveImputedFeatures()
    {
        Assert.Contains("COUNT(*) FILTER (", GenreSql);
        Assert.Contains("COALESCE((t.audio_features ->> 'IsImputed')::boolean, false)", GenreSql);
    }

    // ---- Artista ----

    [Fact]
    public void ArtistSql_ReadsTheArtistsOwnSignals_FromTheArtistsTable_NotFromTheTracksJsonb()
    {
        Assert.Contains("FROM catalog.artists AS a", ArtistSql);
        Assert.Contains("a.popularity", ArtistSql);
        Assert.Contains("a.followers", ArtistSql);
        Assert.DoesNotContain("jsonb_array_elements", ArtistSql);
    }

    [Fact]
    public void ArtistSql_ExcludesUnenrichedByDefault_ButTheFilterIsOptional()
    {
        Assert.Contains("WHERE (@IncludeUnenriched OR a.is_enriched)", ArtistSql);
    }

    [Fact]
    public void ArtistSql_ExposesTheEnrichmentFlag_SoZerosAreNeverMistakenForRealData()
    {
        Assert.Contains("""a.is_enriched   AS "IsEnriched",""", ArtistSql);
    }

    // ---- Álbum ----

    [Fact]
    public void AlbumSql_GroupsTracksByAlbum_AndLeftJoinsTheAlbumForNameAndYear()
    {
        Assert.Contains("GROUP BY t.album_id", AlbumSql);
        Assert.Contains("LEFT JOIN catalog.albums AS al ON al.id = g.album_id", AlbumSql);
    }

    [Fact]
    public void AlbumSql_ExcludesTracksWithoutAnAlbum()
    {
        Assert.Contains("WHERE t.album_id IS NOT NULL", AlbumSql);
    }

    [Fact]
    public void AlbumSql_ReportsTrackCount_SoTheAverageCanBeReadInContext()
    {
        Assert.Contains("""track_count         AS "TrackCount",""", AlbumSql);
    }

    // ---- Ano ----

    [Fact]
    public void YearSql_GroupsByTheIndexedReleaseYearColumn()
    {
        Assert.Contains("GROUP BY al.release_year", YearSql);
    }

    [Fact]
    public void YearSql_LeftJoinsAlbums_SoTracksWithoutAYearFallIntoANullRow()
    {
        Assert.Contains("LEFT JOIN catalog.albums AS al ON al.id = t.album_id", YearSql);
        Assert.DoesNotContain("WHERE t.album_id IS NOT NULL", YearSql);
    }

    [Fact]
    public void YearSql_PutsTheUnknownYearLast_InEveryOrdering()
    {
        Assert.Contains("NULLS LAST", YearSql);
    }

    [Fact]
    public void YearSql_CountsDistinctAlbumsPerYear()
    {
        Assert.Contains("COUNT(DISTINCT t.album_id)", YearSql);
    }

    // ---- Paginação e dialeto, em todas ----

    public static TheoryData<string> AllSql()
    {
        var data = new TheoryData<string>();
        data.Add(GetGenreInsightsQueryHandler.Sql);
        data.Add(GetArtistInsightsQueryHandler.Sql);
        data.Add(GetAlbumInsightsQueryHandler.Sql);
        data.Add(GetAlbumYearInsightsQueryHandler.Sql);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllSql))]
    public void Sql_PaginatesWithRowNumberAndCountOver_SingleRoundTrip(string sql)
    {
        Assert.Contains("ROW_NUMBER() OVER (", sql);
        Assert.Contains("COUNT(*) OVER ()", sql);
        Assert.Contains("WHERE row_number BETWEEN @FirstResult AND @LastResult", sql);
    }

    [Theory]
    [MemberData(nameof(AllSql))]
    public void Sql_ChoosesTheSortBranchByParameter_NoDynamicOrderBy(string sql)
    {
        Assert.Contains("CASE WHEN @SortBy = ", sql);
    }

    [Theory]
    [MemberData(nameof(AllSql))]
    public void Sql_FollowsPostgresConventions_NoSqlServerDialect(string sql)
    {
        Assert.DoesNotContain("NOLOCK", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[", sql);
        Assert.DoesNotContain("TOP ", sql, StringComparison.OrdinalIgnoreCase);
    }
}
