using SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Domain;

/// <summary>
/// Testes do agregado <see cref="Album"/> e do value object <see cref="ReleaseDate"/>, que precisa aguentar
/// as três precisões que a API do Spotify devolve em <c>release_date</c>.
/// </summary>
public sealed class AlbumTests
{
    [Fact]
    public void RegisterFromReference_LeavesTheDetailsUnknown()
    {
        Album album = Album.RegisterFromReference(SpotifyAlbumId.Of("al1"), "A Night at the Opera");

        Assert.Equal("A Night at the Opera", album.Name);
        Assert.Null(album.ReleaseDate);
        Assert.Equal(0, album.TotalTracks);
        Assert.False(album.IsEnriched);
    }

    [Fact]
    public void EnrichDetails_FillsTheDetails()
    {
        Album album = Album.RegisterFromReference(SpotifyAlbumId.Of("al1"), "A Night at the Opera");

        album.EnrichDetails("A Night at the Opera", "1975-11-21", totalTracks: 12);

        Assert.True(album.IsEnriched);
        Assert.Equal(12, album.TotalTracks);
        Assert.Equal(1975, album.ReleaseDate!.Year);
        Assert.Equal(ReleasePrecision.Day, album.ReleaseDate.Precision);
        Assert.Equal(new DateOnly(1975, 11, 21), album.ReleaseDate.ExactDate);
    }

    [Fact]
    public void EnrichDetails_WithNegativeTotalTracks_Throws()
    {
        Album album = Album.RegisterFromReference(SpotifyAlbumId.Of("al1"), "X");

        Assert.Throws<DomainException>(() => album.EnrichDetails("X", "1975", totalTracks: -1));
    }

    [Theory]
    [InlineData("1975", 1975, ReleasePrecision.Year)]
    [InlineData("1975-11", 1975, ReleasePrecision.Month)]
    [InlineData("1975-11-21", 1975, ReleasePrecision.Day)]
    public void ReleaseDate_TryParse_KeepsTheOriginalPrecision(
        string raw, int expectedYear, ReleasePrecision expectedPrecision)
    {
        ReleaseDate? releaseDate = ReleaseDate.TryParse(raw);

        Assert.NotNull(releaseDate);
        Assert.Equal(raw, releaseDate!.Raw);
        Assert.Equal(expectedYear, releaseDate.Year);
        Assert.Equal(expectedPrecision, releaseDate.Precision);
        Assert.Equal(expectedPrecision == ReleasePrecision.Day, releaseDate.ExactDate is not null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("desconhecido")]
    [InlineData("75")]
    public void ReleaseDate_TryParse_ReturnsNull_ForAbsentOrUnrecognizedValues(string? raw)
        => Assert.Null(ReleaseDate.TryParse(raw));
}
