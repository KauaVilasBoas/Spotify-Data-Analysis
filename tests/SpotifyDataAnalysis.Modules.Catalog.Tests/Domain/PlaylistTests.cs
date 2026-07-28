using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists.Events;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Domain;

/// <summary>
/// Testes do agregado <see cref="Playlist"/>. O ponto central é a idempotência de
/// <see cref="Playlist.RecordIngestion"/> — a propriedade que sustenta o job de coleta agendado (E1.6).
/// </summary>
public sealed class PlaylistTests
{
    private static readonly DateTime Now = new(2026, 07, 28, 12, 00, 00, DateTimeKind.Utc);

    private static Playlist NewSeed()
        => Playlist.RegisterAsSeed(SpotifyPlaylistId.Of("pl1"), " Minha semente ", " kaua ");

    private static SpotifyTrackId[] Ids(params string[] values)
        => values.Select(SpotifyTrackId.Of).ToArray();

    [Fact]
    public void RegisterAsSeed_TrimsAndStartsEmpty()
    {
        Playlist playlist = NewSeed();

        Assert.Equal("Minha semente", playlist.Name);
        Assert.Equal("kaua", playlist.OwnerDisplayName);
        Assert.Empty(playlist.TrackIds);
        Assert.Null(playlist.LastIngestedAtUtc);
        Assert.Empty(playlist.DomainEvents);
    }

    [Fact]
    public void RegisterAsSeed_WithBlankOwner_KeepsItNull()
        => Assert.Null(Playlist.RegisterAsSeed(SpotifyPlaylistId.Of("pl1"), "Semente", "   ").OwnerDisplayName);

    [Fact]
    public void RegisterAsSeed_WithBlankName_Throws()
        => Assert.Throws<DomainException>(
            () => Playlist.RegisterAsSeed(SpotifyPlaylistId.Of("pl1"), "  ", null));

    [Fact]
    public void RecordIngestion_StampsTheCycle_AndRaisesTheDomainEvent()
    {
        Playlist playlist = NewSeed();

        playlist.RecordIngestion(Ids("t1", "t2"), Now);

        Assert.Equal(new[] { "t1", "t2" }, playlist.TrackIds);
        Assert.Equal(2, playlist.TrackCount);
        Assert.Equal(Now, playlist.LastIngestedAtUtc);

        var @event = Assert.IsType<PlaylistIngestedDomainEvent>(Assert.Single(playlist.DomainEvents));
        Assert.Equal("pl1", @event.PlaylistId);
        Assert.Equal(2, @event.TrackCount);
        Assert.Equal(Now, @event.OccurredOnUtc);
    }

    [Fact]
    public void RecordIngestion_DeduplicatesTheTrackIds()
    {
        Playlist playlist = NewSeed();

        playlist.RecordIngestion(Ids("t1", "t1", "t2"), Now);

        Assert.Equal(new[] { "t1", "t2" }, playlist.TrackIds);
    }

    [Fact]
    public void RecordIngestion_ReplacesThePreviousSnapshot_SoRemovedTracksDisappear()
    {
        Playlist playlist = NewSeed();
        playlist.RecordIngestion(Ids("t1", "t2", "t3"), Now);

        playlist.RecordIngestion(Ids("t1", "t4"), Now.AddDays(1));

        Assert.Equal(new[] { "t1", "t4" }, playlist.TrackIds);
        Assert.Equal(Now.AddDays(1), playlist.LastIngestedAtUtc);
    }

    [Fact]
    public void RecordIngestion_TwiceWithTheSameInput_LeavesTheSameState()
    {
        Playlist playlist = NewSeed();

        playlist.RecordIngestion(Ids("t1", "t2"), Now);
        playlist.RecordIngestion(Ids("t1", "t2"), Now);

        Assert.Equal(new[] { "t1", "t2" }, playlist.TrackIds);
        Assert.Equal(Now, playlist.LastIngestedAtUtc);
    }

    [Fact]
    public void UpdateMetadata_SyncsNameAndOwner()
    {
        Playlist playlist = NewSeed();

        playlist.UpdateMetadata("Outro nome", null);

        Assert.Equal("Outro nome", playlist.Name);
        Assert.Null(playlist.OwnerDisplayName);
    }
}
