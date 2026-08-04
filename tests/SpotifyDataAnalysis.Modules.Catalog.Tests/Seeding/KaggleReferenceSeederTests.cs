using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Seeding;

/// <summary>
/// O coração testável do seed de referências (E1.11): separação dos créditos, identidade sintética e montagem
/// do vínculo faixa → artistas/álbum. A persistência em lote roda contra o Postgres real no smoke test do card.
/// </summary>
public sealed class KaggleReferenceSeederTests
{
    private static KaggleCatalogRow Row(string? artists, string? albumName = "Album", string id = "t1")
        => new(
            TrackId: id,
            TrackName: "Song",
            Artists: artists,
            AlbumName: albumName,
            Popularity: 50,
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

    // ---- Separação dos créditos ----

    [Fact]
    public void SplitArtists_SplitsOnSemicolon_PreservingSourceOrder()
    {
        IReadOnlyList<string> credits = KaggleReferenceSeeder.SplitArtists("Ingrid Michaelson;ZAYN");

        Assert.Equal(["Ingrid Michaelson", "ZAYN"], credits);
    }

    [Fact]
    public void SplitArtists_TrimsAndDropsEmptyParts()
    {
        IReadOnlyList<string> credits = KaggleReferenceSeeder.SplitArtists("  A  ;; B ;");

        Assert.Equal(["A", "B"], credits);
    }

    [Fact]
    public void SplitArtists_DropsRepeatedCreditWithinTheSameTrack()
    {
        // Crédito repetido geraria dois TrackArtist com a mesma id no jsonb da faixa.
        IReadOnlyList<string> credits = KaggleReferenceSeeder.SplitArtists("Daft Punk;daft punk");

        Assert.Equal(["Daft Punk"], credits);
    }

    [Fact]
    public void SplitArtists_WithoutArtists_ReturnsEmpty()
    {
        Assert.Empty(KaggleReferenceSeeder.SplitArtists(null));
        Assert.Empty(KaggleReferenceSeeder.SplitArtists("   "));
    }

    [Fact]
    public void SplitArtists_KeepsCommaInsideAName()
    {
        // O separador é ';' — vírgula faz parte do nome e não pode quebrá-lo.
        IReadOnlyList<string> credits = KaggleReferenceSeeder.SplitArtists("Earth, Wind & Fire;Chic");

        Assert.Equal(["Earth, Wind & Fire", "Chic"], credits);
    }

    // ---- Identidade sintética ----

    [Fact]
    public void Identity_IsDeterministicAndPrefixed()
    {
        string first = CsvDerivedIdentity.From("Daft Punk");
        string second = CsvDerivedIdentity.From("Daft Punk");

        Assert.Equal(first, second);
        Assert.StartsWith(CsvDerivedIdentity.Prefix, first);
    }

    [Fact]
    public void Identity_IgnoresCaseAndSurroundingWhitespace()
    {
        Assert.Equal(CsvDerivedIdentity.From("Daft Punk"), CsvDerivedIdentity.From("  daft punk  "));
    }

    [Fact]
    public void Identity_DistinguishesDifferentNames()
    {
        Assert.NotEqual(CsvDerivedIdentity.From("Queen"), CsvDerivedIdentity.From("Queens"));
    }

    [Fact]
    public void Identity_DoesNotCollideWhenPartBoundariesShift()
    {
        // Sem separador entre as partes, ("ab","c") e ("a","bc") gerariam a mesma id.
        Assert.NotEqual(CsvDerivedIdentity.From("ab", "c"), CsvDerivedIdentity.From("a", "bc"));
    }

    [Fact]
    public void Identity_FitsTheIdColumn()
    {
        // A coluna id é varchar(64) para artista, álbum e faixa.
        Assert.True(CsvDerivedIdentity.From("Qualquer Artista").Length <= 64);
    }

    // ---- Vínculo faixa → artistas/álbum ----

    [Fact]
    public void TryBuildLink_BuildsCreditsInOrder_WithTheFirstAsPrimary()
    {
        bool built = KaggleReferenceSeeder.TryBuildLink(Row("Ingrid Michaelson;ZAYN"), out var link);

        Assert.True(built);
        Assert.Equal("t1", link.TrackId.Value);
        Assert.Collection(
            link.Credits,
            first => Assert.Equal("Ingrid Michaelson", first.Name),
            second => Assert.Equal("ZAYN", second.Name));
    }

    [Fact]
    public void TryBuildLink_GivesEachCreditTheSyntheticIdOfItsName()
    {
        KaggleReferenceSeeder.TryBuildLink(Row("Daft Punk"), out var link);

        Assert.Equal(CsvDerivedIdentity.From("Daft Punk"), link.Credits[0].Id);
    }

    [Fact]
    public void TryBuildLink_WithoutArtists_IsRejected()
    {
        Assert.False(KaggleReferenceSeeder.TryBuildLink(Row(artists: null), out _));
    }

    [Fact]
    public void TryBuildLink_WithoutTrackId_IsRejected()
    {
        Assert.False(KaggleReferenceSeeder.TryBuildLink(Row("A", id: "  "), out _));
    }

    [Fact]
    public void TryBuildLink_WithoutAlbumName_LeavesTheAlbumNull()
    {
        KaggleReferenceSeeder.TryBuildLink(Row("A", albumName: null), out var link);

        Assert.Null(link.AlbumId);
    }

    [Fact]
    public void AlbumId_IsScopedByPrimaryArtist_SoSameTitledAlbumsDoNotMerge()
    {
        // "Greatest Hits" de artistas diferentes são álbuns diferentes; identificar só pelo nome os fundiria.
        Assert.NotEqual(
            KaggleReferenceSeeder.AlbumIdOf("Queen", "Greatest Hits"),
            KaggleReferenceSeeder.AlbumIdOf("ABBA", "Greatest Hits"));
    }

    [Fact]
    public void AlbumId_IsStableForTheSameArtistAndTitle()
    {
        Assert.Equal(
            KaggleReferenceSeeder.AlbumIdOf("Queen", "Greatest Hits"),
            KaggleReferenceSeeder.AlbumIdOf("queen", "  greatest hits "));
    }

    [Fact]
    public void TryBuildLink_ProducesCreditsUsableAsTrackArtists()
    {
        KaggleReferenceSeeder.TryBuildLink(Row("A;B"), out var link);

        Assert.All(link.Credits, credit =>
        {
            Assert.IsType<TrackArtist>(credit);
            Assert.False(string.IsNullOrWhiteSpace(credit.Id));
            Assert.False(string.IsNullOrWhiteSpace(credit.Name));
        });
    }
}
