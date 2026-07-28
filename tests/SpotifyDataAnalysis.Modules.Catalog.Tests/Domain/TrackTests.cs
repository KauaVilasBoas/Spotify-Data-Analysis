using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks.Events;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Domain;

/// <summary>Testes de domínio do agregado <see cref="Track"/> e seus value objects.</summary>
public sealed class TrackTests
{
    private static Track NewTrack() => Track.Register(
        SpotifyTrackId.Of("abc"), "Song", Popularity.Of(50), 200_000, @explicit: false,
        albumId: "album1", artistIds: new[] { "artist1", "artist2" });

    [Fact]
    public void Register_CreatesTrack_AndRaisesDomainEvent()
    {
        Track track = NewTrack();

        Assert.Equal("abc", track.Id.Value);
        Assert.Equal("Song", track.Name);
        Assert.Equal(50, track.Popularity.Value);
        Assert.Equal(2, track.ArtistIds.Count);
        Assert.Null(track.AudioFeatures);

        Assert.Single(track.DomainEvents);
        Assert.IsType<TrackRegisteredDomainEvent>(track.DomainEvents[0]);
    }

    [Fact]
    public void Register_WithBlankName_Throws()
        => Assert.Throws<DomainException>(() => Track.Register(
            SpotifyTrackId.Of("abc"), "  ", Popularity.Of(10), 1, false, null, Array.Empty<string>()));

    [Fact]
    public void Register_WithNegativeDuration_Throws()
        => Assert.Throws<DomainException>(() => Track.Register(
            SpotifyTrackId.Of("abc"), "Song", Popularity.Of(10), -1, false, null, Array.Empty<string>()));

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
    public void EmptyTrackId_Throws()
        => Assert.Throws<DomainException>(() => SpotifyTrackId.Of("  "));
}
