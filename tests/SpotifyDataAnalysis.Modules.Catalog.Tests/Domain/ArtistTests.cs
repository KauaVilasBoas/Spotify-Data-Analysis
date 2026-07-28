using SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Domain;

/// <summary>
/// Testes do agregado <see cref="Artist"/>, com foco na distinção entre "descoberto por referência" e
/// "perfil carregado" — é ela que impede o modelo de ML (E3) tratar ausência de dado como popularidade zero.
/// </summary>
public sealed class ArtistTests
{
    [Fact]
    public void RegisterFromReference_LeavesTheProfileUnknown()
    {
        Artist artist = Artist.RegisterFromReference(SpotifyArtistId.Of("a1"), "  Queen  ");

        Assert.Equal("a1", artist.Id.Value);
        Assert.Equal("Queen", artist.Name);
        Assert.Equal(Popularity.Unknown, artist.Popularity);
        Assert.Equal(0, artist.Followers);
        Assert.Empty(artist.Genres);
        Assert.False(artist.IsEnriched);
    }

    [Fact]
    public void RegisterFromReference_WithBlankName_Throws()
        => Assert.Throws<DomainException>(
            () => Artist.RegisterFromReference(SpotifyArtistId.Of("a1"), "   "));

    [Fact]
    public void EnrichProfile_FillsTheProfile_AndDropsBlankGenres()
    {
        Artist artist = Artist.RegisterFromReference(SpotifyArtistId.Of("a1"), "Queen");

        artist.EnrichProfile("Queen", Popularity.Of(87), followers: 45_000_000, genres: ["rock", "  ", " glam rock "]);

        Assert.True(artist.IsEnriched);
        Assert.Equal(87, artist.Popularity.Value);
        Assert.Equal(45_000_000, artist.Followers);
        Assert.Equal(new[] { "rock", "glam rock" }, artist.Genres);
    }

    [Fact]
    public void EnrichProfile_IsIdempotent_AndReplacesTheGenres()
    {
        Artist artist = Artist.RegisterFromReference(SpotifyArtistId.Of("a1"), "Queen");

        artist.EnrichProfile("Queen", Popularity.Of(80), 10, ["rock"]);
        artist.EnrichProfile("Queen", Popularity.Of(90), 20, ["pop"]);

        Assert.Equal(90, artist.Popularity.Value);
        Assert.Equal(20, artist.Followers);
        Assert.Equal(new[] { "pop" }, artist.Genres);
    }

    [Fact]
    public void EnrichProfile_WithNegativeFollowers_Throws()
    {
        Artist artist = Artist.RegisterFromReference(SpotifyArtistId.Of("a1"), "Queen");

        Assert.Throws<DomainException>(
            () => artist.EnrichProfile("Queen", Popularity.Of(80), followers: -1, genres: []));
    }

    [Fact]
    public void Rename_DoesNotResetTheProfile()
    {
        Artist artist = Artist.RegisterFromReference(SpotifyArtistId.Of("a1"), "Queen");
        artist.EnrichProfile("Queen", Popularity.Of(80), 10, ["rock"]);

        artist.Rename("Queen (UK)");

        Assert.Equal("Queen (UK)", artist.Name);
        Assert.True(artist.IsEnriched);
        Assert.Equal(80, artist.Popularity.Value);
    }
}
