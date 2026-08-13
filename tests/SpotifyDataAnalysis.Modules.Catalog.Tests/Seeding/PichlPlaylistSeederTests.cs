using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Seeding;

/// <summary>
/// O coração testável do seed de playlists do Pichl (E4.5): o parser do dialeto do CSV (escape por barra
/// invertida, header com espaço), a reconciliação por <c>TrackMatchKey</c> com a política de colisão, e o
/// acumulador (dedup + teto). A persistência em lote e os gates (taxa de casamento, densidade de co-ocorrência)
/// rodam contra o Postgres real no smoke test do card.
/// </summary>
public sealed class PichlPlaylistSeederTests
{
    // ----- Parser do CSV do Pichl -----------------------------------------------------------------

    [Fact]
    public async Task Reader_ParsesTheFourColumns_ByPosition_IgnoringHeaderSpacing()
    {
        // Header com espaço após a vírgula (como o arquivo real); dados sem espaço, tudo entre aspas.
        const string csv =
            "\"user_id\", \"artistname\", \"trackname\", \"playlistname\"\n" +
            "\"u1\",\"Elvis Costello\",\"Alison\",\"HARD ROCK 2010\"\n";

        List<PichlPlaylistRow> rows = await ParseAll(csv);

        PichlPlaylistRow parsed = Assert.Single(rows);
        Assert.Equal("u1", parsed.UserId);
        Assert.Equal("Elvis Costello", parsed.ArtistName);
        Assert.Equal("Alison", parsed.TrackName);
        Assert.Equal("HARD ROCK 2010", parsed.PlaylistName);
    }

    [Fact]
    public async Task Reader_HandlesBackslashEscapedQuote_WithoutDesyncing()
    {
        // O dialeto do Pichl escapa aspas com "\" (não com aspa duplicada). Uma aspa escapada no meio do título
        // não pode desalinhar o parser: a linha seguinte precisa continuar íntegra.
        const string csv =
            "\"user_id\", \"artistname\", \"trackname\", \"playlistname\"\n" +
            "\"u1\",\"AC/DC\",\"She said \\\"yes\\\"\",\"rock\"\n" +
            "\"u1\",\"Queen\",\"Bohemian Rhapsody\",\"rock\"\n";

        List<PichlPlaylistRow> rows = await ParseAll(csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal("She said \"yes\"", rows[0].TrackName);
        Assert.Equal("Queen", rows[1].ArtistName);
        Assert.Equal("Bohemian Rhapsody", rows[1].TrackName);
    }

    [Fact]
    public async Task Reader_SkipsRows_WithoutUserOrPlaylist()
    {
        const string csv =
            "\"user_id\", \"artistname\", \"trackname\", \"playlistname\"\n" +
            "\"\",\"Artist\",\"Track\",\"list\"\n" +   // sem user_id
            "\"u1\",\"Artist\",\"Track\",\"\"\n" +      // sem playlistname
            "\"u1\",\"Artist\",\"Track\",\"list\"\n";   // válida

        List<PichlPlaylistRow> rows = await ParseAll(csv);

        PichlPlaylistRow kept = Assert.Single(rows);
        Assert.Equal("u1", kept.UserId);
        Assert.Equal("list", kept.PlaylistName);
    }

    // ----- Reconciliação por TrackMatchKey (mapa do dataset.csv) ----------------------------------

    [Fact]
    public async Task MatchIndex_ResolvesPichlRow_ByArtistAndTitle()
    {
        CatalogMatchKeyIndex index = await BuildIndex(
            CatalogRow("t1", artists: "Queen", trackName: "Bohemian Rhapsody"));

        // O Pichl casa por From(trackname, artistname); o editorial suffix e a caixa são normalizados dos dois lados.
        Assert.Equal("t1", index.ResolveTrackId("Bohemian Rhapsody - Remastered 2011", "Queen"));
        Assert.Equal(1, index.ResolvableKeys);
        Assert.Equal(0, index.AmbiguousKeysDiscarded);
    }

    [Fact]
    public async Task MatchIndex_TreatsSameTrackIdRepeatedPerGenre_AsNotAmbiguous()
    {
        // A mesma faixa aparece uma vez por gênero no dataset (mesmo track_id) — isso NÃO é colisão.
        CatalogMatchKeyIndex index = await BuildIndex(
            CatalogRow("t1", artists: "Queen", trackName: "Bohemian Rhapsody"),
            CatalogRow("t1", artists: "Queen", trackName: "Bohemian Rhapsody"));

        Assert.Equal("t1", index.ResolveTrackId("Bohemian Rhapsody", "Queen"));
        Assert.Equal(1, index.ResolvableKeys);
        Assert.Equal(0, index.AmbiguousKeysDiscarded);
    }

    [Fact]
    public async Task MatchIndex_DiscardsKey_WhenItMapsToDistinctTrackIds()
    {
        // Dois track_ids distintos sob a mesma chave "artista|título": ambígua -> descartada (política DP-2).
        CatalogMatchKeyIndex index = await BuildIndex(
            CatalogRow("t1", artists: "The Band", trackName: "The Weight"),
            CatalogRow("t2", artists: "The Band", trackName: "The Weight"));

        Assert.Null(index.ResolveTrackId("The Weight", "The Band"));
        Assert.Equal(0, index.ResolvableKeys);
        Assert.Equal(1, index.AmbiguousKeysDiscarded);
    }

    [Fact]
    public async Task MatchIndex_UsesOnlyPrimaryArtist_FromCatalogArtistList()
    {
        // FromArtistList pega o primeiro artista (";"-separado) do lado do catálogo; o Pichl traz um só.
        CatalogMatchKeyIndex index = await BuildIndex(
            CatalogRow("t1", artists: "Elton John;Dua Lipa", trackName: "Cold Heart"));

        Assert.Equal("t1", index.ResolveTrackId("Cold Heart", "Elton John"));
    }

    [Fact]
    public async Task MatchIndex_ReturnsNull_ForTrackNotInCatalog()
    {
        CatalogMatchKeyIndex index = await BuildIndex(
            CatalogRow("t1", artists: "Queen", trackName: "Bohemian Rhapsody"));

        Assert.Null(index.ResolveTrackId("Nonexistent Song", "Nobody"));
    }

    // ----- Acumulador -----------------------------------------------------------------------------

    [Fact]
    public void Accumulator_DedupesTrackIds_PreservingFirstAppearanceOrder()
    {
        var accumulator = new PlaylistAccumulator(maxPlaylists: 0);

        accumulator.AddTrack("p1", "My List", "a");
        accumulator.AddTrack("p1", "My List", "b");
        accumulator.AddTrack("p1", "My List", "a"); // repetida

        AccumulatedPlaylist playlist = Assert.Single(accumulator.Drain());
        Assert.Equal(new[] { "a", "b" }, playlist.TrackIds);
    }

    [Fact]
    public void Accumulator_EnforcesMaxPlaylists_ButKeepsFeedingOpenOnes()
    {
        var accumulator = new PlaylistAccumulator(maxPlaylists: 1);

        accumulator.AddTrack("p1", "First", "a");
        accumulator.AddTrack("p2", "Second", "b"); // teto atingido -> ignorada
        accumulator.AddTrack("p1", "First", "c");   // playlist já aberta continua recebendo

        AccumulatedPlaylist playlist = Assert.Single(accumulator.Drain());
        Assert.Equal("p1", playlist.PlaylistId);
        Assert.Equal(new[] { "a", "c" }, playlist.TrackIds);
    }

    [Fact]
    public void Accumulator_TouchedPlaylistWithoutMatches_HasNoTracks()
    {
        var accumulator = new PlaylistAccumulator(maxPlaylists: 0);

        accumulator.TouchPlaylist("p1", "Empty");

        AccumulatedPlaylist playlist = Assert.Single(accumulator.Drain());
        Assert.Empty(playlist.TrackIds);
    }

    [Fact]
    public void Accumulator_Drain_EmptiesTheAccumulator()
    {
        var accumulator = new PlaylistAccumulator(maxPlaylists: 0);
        accumulator.AddTrack("p1", "List", "a");

        _ = accumulator.Drain().ToList();

        Assert.Empty(accumulator.Drain());
        Assert.Equal(0, accumulator.PlaylistsSeen);
    }

    // ----- Helpers --------------------------------------------------------------------------------

    private static async Task<List<PichlPlaylistRow>> ParseAll(string csv)
    {
        using var reader = new StringReader(csv);
        var rows = new List<PichlPlaylistRow>();
        await foreach (PichlPlaylistRow row in PichlPlaylistCsvReader.ParseAsync(reader))
            rows.Add(row);
        return rows;
    }

    /// <summary>Uma linha mínima do dataset.csv com só as colunas que a reconciliação usa (track_id, artists, track_name).</summary>
    private static string CatalogRow(string trackId, string artists, string trackName) =>
        $"0,{trackId},{artists},Album,{trackName},50,200000,False,0.5,0.6,5,-6.0,1,0.05,0.1,0.0,0.2,0.4,120.0,4,pop";

    private static async Task<CatalogMatchKeyIndex> BuildIndex(params string[] dataRows)
    {
        const string header =
            ",track_id,artists,album_name,track_name,popularity,duration_ms,explicit,danceability,energy,key," +
            "loudness,mode,speechiness,acousticness,instrumentalness,liveness,valence,tempo,time_signature,track_genre";

        string path = Path.Combine(Path.GetTempPath(), $"pichl-catalog-{Guid.NewGuid():N}.csv");
        await File.WriteAllLinesAsync(path, new[] { header }.Concat(dataRows));

        try
        {
            return await CatalogMatchKeyIndex.BuildFromCatalogDatasetAsync(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
