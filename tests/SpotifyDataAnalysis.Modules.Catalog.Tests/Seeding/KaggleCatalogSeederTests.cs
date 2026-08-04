using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Seeding;

/// <summary>
/// O coração testável do seed do catálogo (E1.10): a montagem de uma faixa a partir de uma linha do CSV, com a
/// regra de completude. A persistência em lote e o dedupe rodam contra o Postgres real no smoke test do card.
/// </summary>
public sealed class KaggleCatalogSeederTests
{
    private static KaggleCatalogRow CompleteRow(string id = "t1", int? popularity = 50)
        => new(
            TrackId: id,
            TrackName: "Song",
            Artists: "Artist A;Artist B",
            AlbumName: "Album",
            Popularity: popularity,
            DurationMs: 200_000,
            Explicit: false,
            Danceability: 0.5,
            Energy: 0.6,
            Valence: 0.4,
            Tempo: 120.0,
            Acousticness: 0.1,
            Instrumentalness: 0.0,
            Liveness: 0.2,
            Speechiness: 0.05,
            Loudness: -6.0,
            Key: 5,
            Mode: 1,
            TimeSignature: 4,
            Genre: "pop");

    [Fact]
    public void TryCreateTrack_WithCompleteMeasuredRow_BuildsTheTrack()
    {
        bool created = KaggleCatalogSeeder.TryCreateTrack(CompleteRow(), out Track? track);

        Assert.True(created);
        Assert.NotNull(track);
        Assert.Equal("Song", track!.Name);
        Assert.Equal(50, track.Popularity.Value);
        Assert.Empty(track.Artists); // o CSV traz NOME de artista, não SpotifyArtistId — créditos ficam vazios
        Assert.NotNull(track.AudioFeatures);
        Assert.Equal("pop", track.AudioFeatures!.Genre);
        Assert.False(track.AudioFeatures.IsImputed); // dados medidos
    }

    [Fact]
    public void TryCreateTrack_WithAMissingNumericFeature_IsIncomplete()
    {
        KaggleCatalogRow row = CompleteRow() with { Loudness = null };

        Assert.False(KaggleCatalogSeeder.TryCreateTrack(row, out Track? track));
        Assert.Null(track);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(101)]
    public void TryCreateTrack_WithMissingOrOutOfRangePopularity_IsIncomplete(int? popularity)
    {
        KaggleCatalogRow row = CompleteRow(popularity: popularity);

        Assert.False(KaggleCatalogSeeder.TryCreateTrack(row, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreateTrack_WithBlankId_IsIncomplete(string id)
        => Assert.False(KaggleCatalogSeeder.TryCreateTrack(CompleteRow(id: id), out _));

    [Fact]
    public void TryCreateTrack_WithBlankName_IsIncomplete()
    {
        KaggleCatalogRow row = CompleteRow() with { TrackName = "  " };

        Assert.False(KaggleCatalogSeeder.TryCreateTrack(row, out _));
    }

    [Fact]
    public async Task Reader_ParsesTheKaggleColumns_ByHeaderName()
    {
        // Header do dataset real (a 1ª coluna é um índice sem nome — ignorada por leitura via nome).
        const string csv =
            ",track_id,artists,album_name,track_name,popularity,duration_ms,explicit,danceability,energy,key," +
            "loudness,mode,speechiness,acousticness,instrumentalness,liveness,valence,tempo,time_signature,track_genre\n" +
            "0,abc123,Artist A;Artist B,Some Album,Some Song,73,201000,False,0.5,0.6,5,-6.0,1,0.05,0.1,0.0,0.2,0.4,120.0,4,pop\n";

        using var reader = new StringReader(csv);
        var rows = new List<KaggleCatalogRow>();
        await foreach (KaggleCatalogRow row in KaggleCatalogCsvReader.ParseAsync(reader))
            rows.Add(row);

        KaggleCatalogRow parsed = Assert.Single(rows);
        Assert.Equal("abc123", parsed.TrackId);
        Assert.Equal("Some Song", parsed.TrackName);
        Assert.Equal(73, parsed.Popularity);
        Assert.Equal(201_000, parsed.DurationMs);
        Assert.False(parsed.Explicit);
        Assert.Equal(-6.0, parsed.Loudness);
        Assert.Equal(4, parsed.TimeSignature);
        Assert.Equal("pop", parsed.Genre);
    }
}
