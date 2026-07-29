using SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks.Events;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Domain;

/// <summary>Testes de domínio do agregado <see cref="Track"/> e seus value objects.</summary>
public sealed class TrackTests
{
    private static Track NewTrack(Isrc? isrc = null) => Track.Register(
        SpotifyTrackId.Of("abc"), "Song", Popularity.Of(50), 200_000, @explicit: false,
        albumId: "album1", artists: [TrackArtist.Of("artist1", "Queen"), TrackArtist.Of("artist2", "Bowie")],
        isrc: isrc);

    [Fact]
    public void Register_CreatesTrack_AndRaisesDomainEvent()
    {
        Track track = NewTrack();

        Assert.Equal("abc", track.Id.Value);
        Assert.Equal("Song", track.Name);
        Assert.Equal(50, track.Popularity.Value);
        Assert.Equal(new[] { "artist1", "artist2" }, track.ArtistIds);
        Assert.Equal("Queen", track.PrimaryArtist!.Name);
        Assert.Null(track.AudioFeatures);

        Assert.Single(track.DomainEvents);
        Assert.IsType<TrackRegisteredDomainEvent>(track.DomainEvents[0]);
    }

    [Fact]
    public void Register_WithoutArtists_HasNoPrimaryArtist()
        => Assert.Null(Track.Register(
            SpotifyTrackId.Of("abc"), "Song", Popularity.Of(10), 1, false, null, [], null).PrimaryArtist);

    [Fact]
    public void Register_WithBlankName_Throws()
        => Assert.Throws<DomainException>(() => Track.Register(
            SpotifyTrackId.Of("abc"), "  ", Popularity.Of(10), 1, false, null, [], null));

    [Fact]
    public void Register_WithNegativeDuration_Throws()
        => Assert.Throws<DomainException>(() => Track.Register(
            SpotifyTrackId.Of("abc"), "Song", Popularity.Of(10), -1, false, null, [], null));

    [Fact]
    public void RefreshFromSource_UpdatesTheSnapshot_WithoutRaisingRegisteredAgain()
    {
        Track track = NewTrack();
        track.ClearDomainEvents();

        track.RefreshFromSource(
            "Song (Remastered)", Popularity.Of(88), 210_000, @explicit: true,
            albumId: "album2", artists: [TrackArtist.Of("artist3", "Bowie")], isrc: null);

        Assert.Equal("Song (Remastered)", track.Name);
        Assert.Equal(88, track.Popularity.Value);
        Assert.Equal(210_000, track.DurationMs);
        Assert.True(track.Explicit);
        Assert.Equal("album2", track.AlbumId);
        Assert.Equal(new[] { "artist3" }, track.ArtistIds);
        Assert.Empty(track.DomainEvents);
    }

    [Fact]
    public void Register_WithIsrc_KeepsTheCode()
        => Assert.Equal("BRBMG0300729", NewTrack(Isrc.Of("BRBMG0300729")).Isrc!.Value);

    [Fact]
    public void Register_WithoutIsrc_LeavesItAbsent()
        => Assert.Null(NewTrack().Isrc);

    [Fact]
    public void RefreshFromSource_AttachesTheIsrc_WhenTheSourceStartsReportingIt()
    {
        Track track = NewTrack();
        Assert.Null(track.Isrc);

        Refresh(track, Isrc.Of("BRBMG0300729"));

        Assert.Equal("BRBMG0300729", track.Isrc!.Value);
    }

    /// <summary>
    /// O ISRC identifica a gravação e não é um retrato: uma resposta da API sem o campo significa "não
    /// informado", não "perdeu o código". Apagar o valor conhecido seria perda de dado.
    /// </summary>
    [Fact]
    public void RefreshFromSource_WithoutIsrc_PreservesTheOneAlreadyKnown()
    {
        Track track = NewTrack(Isrc.Of("BRBMG0300729"));

        Refresh(track, isrc: null);

        Assert.Equal("BRBMG0300729", track.Isrc!.Value);
    }

    /// <summary>
    /// Reaplica sobre a faixa o retrato que ela já tem, mudando só o ISRC. Passar <c>track.Artists</c> —
    /// uma view sobre a coleção interna — é proposital: garante que o agregado materialize a sequência
    /// recebida antes de limpar a própria lista.
    /// </summary>
    private static void Refresh(Track track, Isrc? isrc) => track.RefreshFromSource(
        track.Name, track.Popularity, track.DurationMs, track.Explicit, track.AlbumId, track.Artists, isrc);

    [Fact]
    public void RefreshFromSource_WithTheAggregatesOwnArtists_DoesNotWipeThem()
    {
        Track track = NewTrack();

        Refresh(track, isrc: null);

        Assert.Equal(new[] { "artist1", "artist2" }, track.ArtistIds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Popularity_OutOfRange_Throws(int value)
        => Assert.Throws<DomainException>(() => Popularity.Of(value));

    [Fact]
    public void AttachAudioFeatures_SetsFeatures()
    {
        Track track = NewTrack();
        AudioFeatures features = AudioFeatures.Create(
            danceability: 0.8, energy: 0.6, valence: 0.5, tempo: 120, acousticness: 0.1,
            instrumentalness: 0.0, liveness: 0.2, speechiness: 0.05, loudness: -5.0,
            key: 5, mode: 1, timeSignature: 4, source: "kaggle");

        track.AttachAudioFeatures(features);

        Assert.NotNull(track.AudioFeatures);
        Assert.Equal("kaggle", track.AudioFeatures!.Source);
        Assert.False(track.AudioFeatures.IsImputed);
    }

    [Fact]
    public void SpotifyTrackId_WithSameValue_AreEqual()
        => Assert.Equal(SpotifyTrackId.Of("x"), SpotifyTrackId.Of("x"));

    [Fact]
    public void SpotifyIds_OfDifferentResources_AreNeverEqual()
        => Assert.NotEqual<object>(SpotifyTrackId.Of("x"), SpotifyArtistId.Of("x"));

    [Fact]
    public void EmptyTrackId_Throws()
        => Assert.Throws<DomainException>(() => SpotifyTrackId.Of("  "));
}
