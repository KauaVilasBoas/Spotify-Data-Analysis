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

    [Fact]
    public void RegisterFromReference_StartsWithNoEnrichmentAttempts()
    {
        Artist artist = Artist.RegisterFromReference(SpotifyArtistId.Of("a1"), "Queen");

        Assert.Equal(0, artist.EnrichmentAttempts);
        Assert.Null(artist.LastEnrichmentAttemptUtc);
    }

    [Fact]
    public void RecordEnrichmentMiss_IncrementsTheCounter_AndStampsTheAttempt_WithoutTouchingTheProfile()
    {
        Artist artist = Artist.RegisterFromReference(SpotifyArtistId.Of("a1"), "Queen");
        var when = new DateTime(2026, 07, 29, 12, 0, 0, DateTimeKind.Utc);

        artist.RecordEnrichmentMiss(when);

        Assert.Equal(1, artist.EnrichmentAttempts);
        Assert.Equal(when, artist.LastEnrichmentAttemptUtc);
        // Registrar uma tentativa falha não enriquece o artista — ele segue como referência.
        Assert.False(artist.IsEnriched);
        Assert.Equal(Popularity.Unknown, artist.Popularity);
    }

    [Fact]
    public void RecordEnrichmentMiss_Accumulates_AcrossAttempts()
    {
        Artist artist = Artist.RegisterFromReference(SpotifyArtistId.Of("a1"), "Queen");

        artist.RecordEnrichmentMiss(new DateTime(2026, 07, 29, 0, 0, 0, DateTimeKind.Utc));
        artist.RecordEnrichmentMiss(new DateTime(2026, 07, 30, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, artist.EnrichmentAttempts);
        Assert.Equal(new DateTime(2026, 07, 30, 0, 0, 0, DateTimeKind.Utc), artist.LastEnrichmentAttemptUtc);
    }
}
