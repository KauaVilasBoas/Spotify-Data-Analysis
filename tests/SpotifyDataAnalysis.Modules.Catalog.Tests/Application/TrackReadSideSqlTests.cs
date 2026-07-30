using SpotifyDataAnalysis.Modules.Catalog.Application.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Application;

/// <summary>
/// Contrato do SQL do read-side de faixas, afirmado sobre o texto das queries (sem Postgres vivo).
/// </summary>
public sealed class TrackReadSideSqlTests
{
    private static readonly string SearchSql = SearchTracksQueryHandler.Sql;
    private static readonly string DetailSql = GetTrackByIdQueryHandler.Sql;

    [Fact]
    public void SearchSql_IsParameterized_AndCaseInsensitive()
    {
        Assert.Contains("t.name ILIKE '%' || @Search || '%'", SearchSql);
        Assert.DoesNotContain("LIKE '%' + ", SearchSql);
    }

    [Fact]
    public void SearchSql_IsOptional_SoAnAbsentTermListsTheWholeCatalog()
    {
        Assert.Contains("@Search IS NULL", SearchSql);
    }

    [Fact]
    public void SearchSql_MatchesAnyArtistCredit_NotOnlyThePrimaryOne()
    {
        Assert.Contains("FROM jsonb_array_elements(t.artists) AS credit", SearchSql);
        Assert.Contains("credit ->> 'Name' ILIKE '%' || @Search || '%'", SearchSql);
    }

    [Fact]
    public void SearchSql_PaginatesWithRowNumberAndCountOver_SingleRoundTrip()
    {
        Assert.Contains("ROW_NUMBER() OVER (", SearchSql);
        Assert.Contains("COUNT(*) OVER ()", SearchSql);
        Assert.Contains("WHERE row_number BETWEEN @FirstResult AND @LastResult", SearchSql);
    }

    [Fact]
    public void SearchSql_ChoosesTheSortBranchByParameter_NoDynamicOrderBy()
    {
        Assert.Contains("CASE WHEN @SortByPopularity THEN t.popularity END DESC", SearchSql);
        Assert.Contains("CASE WHEN @SortByPopularity THEN NULL ELSE t.name END ASC", SearchSql);
    }

    [Fact]
    public void SearchSql_BreaksTiesById_SoPaginationIsStableInBothSortOrders()
    {
        Assert.Contains("t.id ASC", SearchSql);
    }

    [Fact]
    public void SearchSql_ExtractsPrimaryArtist_FromFirstElementOfArtistsJsonbArray()
    {
        Assert.Contains("t.artists -> 0 ->> 'Name'", SearchSql);
    }

    [Fact]
    public void SearchSql_FlagsAudioFeaturePresence_WithoutReturningTheFeatures()
    {
        Assert.Contains("""(t.audio_features IS NOT NULL)              AS "HasAudioFeatures",""", SearchSql);
        Assert.DoesNotContain("'Danceability'", SearchSql);
    }

    [Fact]
    public void DetailSql_LooksUpByParameterizedId()
    {
        Assert.Contains("WHERE t.id = @TrackId", DetailSql);
    }

    [Fact]
    public void DetailSql_ReturnsBothJsonbColumnsAsText_ForDeserializationInTheHandler()
    {
        Assert.Contains("""t.artists::text         AS "ArtistsJson",""", DetailSql);
        Assert.Contains("""t.audio_features::text  AS "AudioFeaturesJson""", DetailSql);
    }

    [Fact]
    public void DetailSql_SelectsTheCatalogColumnsTheContractPromises()
    {
        Assert.Contains("t.popularity", DetailSql);
        Assert.Contains("t.duration_ms", DetailSql);
        Assert.Contains("t.explicit", DetailSql);
        Assert.Contains("t.isrc", DetailSql);
        Assert.Contains("t.album_id", DetailSql);
    }

    [Fact]
    public void DetailSql_DoesNotFilterOutTracksWithoutAudioFeatures()
    {
        Assert.DoesNotContain("audio_features IS NOT NULL", DetailSql);
    }

    [Theory]
    [InlineData(nameof(SearchTracksQueryHandler))]
    [InlineData(nameof(GetTrackByIdQueryHandler))]
    public void Sql_FollowsPostgresConventions_NoSqlServerDialect(string handler)
    {
        string sql = handler == nameof(SearchTracksQueryHandler) ? SearchSql : DetailSql;

        Assert.DoesNotContain("WITH(NOLOCK)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NOLOCK", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[", sql);
        Assert.DoesNotContain("TOP ", sql, StringComparison.OrdinalIgnoreCase);
    }
}
