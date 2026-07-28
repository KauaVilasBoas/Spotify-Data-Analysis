using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.Modules.Catalog.Contracts.Events;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists.Events;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks.Events;
using SpotifyDataAnalysis.Modules.Catalog.Tests.Fakes;
using SpotifyDataAnalysis.SharedKernel.Exceptions;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Application;

/// <summary>
/// Testes do <see cref="IngestPlaylistCommandHandler"/> — ingestão idempotente por SpotifyTrackId, registro
/// da playlist-semente e das referências (artista/álbum) — e dos translators DomainEvent → IntegrationEvent,
/// com cliente Spotify e repositórios fakes (sem rede/banco).
/// </summary>
public sealed class IngestPlaylistCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 07, 28, 12, 00, 00, DateTimeKind.Utc);

    private sealed record Harness(
        IngestPlaylistCommandHandler Handler,
        InMemoryTrackRepository Tracks,
        InMemoryArtistRepository Artists,
        InMemoryAlbumRepository Albums,
        InMemoryPlaylistRepository Playlists);

    private static Harness Build(FakeSpotifyClient client, InMemoryTrackRepository? tracks = null)
    {
        tracks ??= new InMemoryTrackRepository();
        var artists = new InMemoryArtistRepository();
        var albums = new InMemoryAlbumRepository();
        var playlists = new InMemoryPlaylistRepository();

        var handler = new IngestPlaylistCommandHandler(
            client, tracks, playlists, new CatalogReferenceRegistrar(artists, albums), new FixedClock(Now));

        return new Harness(handler, tracks, artists, albums, playlists);
    }

    private static SpotifyTrack Dto(
        string id, string name, int popularity = 50,
        IReadOnlyList<SpotifyArtistRef>? artists = null, SpotifyAlbumRef? album = null)
        => new(id, name, popularity, DurationMs: 1000, Explicit: false, artists ?? [], album);

    [Fact]
    public async Task Ingests_NewTracks_AndRecordsTheSeedPlaylist()
    {
        Harness harness = Build(new FakeSpotifyClient(Dto("t1", "One"), Dto("t2", "Two")));

        IngestPlaylistResult result = await harness.Handler.HandleAsync(new IngestPlaylistCommand("pl1"));

        Assert.Equal(2, result.Ingested);
        Assert.Equal(0, result.Updated);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Cataloged);
        Assert.Equal(2, harness.Tracks.Store.Count);

        Playlist playlist = harness.Playlists.Store["pl1"];
        Assert.Equal("Minha semente", playlist.Name);
        Assert.Equal("kaua", playlist.OwnerDisplayName);
        Assert.Equal(new[] { "t1", "t2" }, playlist.TrackIds);
        Assert.Equal(Now, playlist.LastIngestedAtUtc);
        Assert.Contains(playlist.DomainEvents, @event => @event is PlaylistIngestedDomainEvent);
    }

    [Fact]
    public async Task Registers_ArtistsAndAlbums_ReferencedByTheTracks()
    {
        var client = new FakeSpotifyClient(
            Dto("t1", "One", artists: [new SpotifyArtistRef("a1", "Queen")], album: new SpotifyAlbumRef("al1", "A Night at the Opera")),
            Dto("t2", "Two", artists: [new SpotifyArtistRef("a1", "Queen"), new SpotifyArtistRef("a2", "Bowie")]));

        Harness harness = Build(client);

        await harness.Handler.HandleAsync(new IngestPlaylistCommand("pl1"));

        Assert.Equal(2, harness.Artists.Store.Count);
        Assert.Equal("Queen", harness.Artists.Store["a1"].Name);
        // Vindo apenas da referência embutida na faixa, o perfil ainda não foi carregado.
        Assert.False(harness.Artists.Store["a1"].IsEnriched);

        Assert.Single(harness.Albums.Store);
        Assert.Equal("A Night at the Opera", harness.Albums.Store["al1"].Name);

        Assert.Equal(2, harness.Tracks.Store["t2"].Artists.Count);
        Assert.Equal("Queen", harness.Tracks.Store["t2"].PrimaryArtist!.Name);
    }

    [Fact]
    public async Task Refreshes_ExistingTrack_InsteadOfDuplicatingIt()
    {
        var tracks = new InMemoryTrackRepository();
        tracks.Seed(CatalogFixtures.Track("t1", "One", popularity: 10));

        Harness harness = Build(new FakeSpotifyClient(Dto("t1", "One (Remastered)", popularity: 90)), tracks);

        IngestPlaylistResult result = await harness.Handler.HandleAsync(new IngestPlaylistCommand("pl1"));

        Assert.Equal(0, result.Ingested);
        Assert.Equal(1, result.Updated);
        Assert.Equal(90, tracks.Store["t1"].Popularity.Value);
        Assert.Equal("One (Remastered)", tracks.Store["t1"].Name);
        // Reingestão não pode reemitir TrackRegistered (o Outbox reprocessaria a cada ciclo).
        Assert.Single(tracks.Store["t1"].DomainEvents);
    }

    [Fact]
    public async Task Skips_TracksWithoutIdOrName()
    {
        Harness harness = Build(new FakeSpotifyClient(Dto("", "Local file"), Dto("t9", "   "), Dto("t1", "One")));

        IngestPlaylistResult result = await harness.Handler.HandleAsync(new IngestPlaylistCommand("pl1"));

        Assert.Equal(1, result.Ingested);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public async Task Counts_TracksRepeatedWithinThePlaylist_AsDuplicates()
    {
        Harness harness = Build(new FakeSpotifyClient(Dto("t1", "One"), Dto("t1", "One"), Dto("t2", "Two")));

        IngestPlaylistResult result = await harness.Handler.HandleAsync(new IngestPlaylistCommand("pl1"));

        Assert.Equal(2, result.Ingested);
        Assert.Equal(1, result.Duplicates);
        Assert.Equal(3, result.Total);
        Assert.Equal(new[] { "t1", "t2" }, harness.Playlists.Store["pl1"].TrackIds);
    }

    [Fact]
    public async Task Fails_WhenThePlaylistDoesNotExistOnSpotify()
    {
        Harness harness = Build(new FakeSpotifyClient(Dto("t1", "One")) { Playlist = null });

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Handler.HandleAsync(new IngestPlaylistCommand("pl1")));
    }

    [Fact]
    public async Task ReRunningTheSameCollection_IsIdempotent()
    {
        var client = new FakeSpotifyClient(Dto("t1", "One"), Dto("t2", "Two"));
        Harness harness = Build(client);

        await harness.Handler.HandleAsync(new IngestPlaylistCommand("pl1"));
        IngestPlaylistResult second = await harness.Handler.HandleAsync(new IngestPlaylistCommand("pl1"));

        Assert.Equal(0, second.Ingested);
        Assert.Equal(2, second.Updated);
        Assert.Equal(2, harness.Tracks.Store.Count);
        Assert.Single(harness.Playlists.Store);
        Assert.Equal(2, harness.Playlists.Store["pl1"].TrackCount);
    }

    [Fact]
    public void TrackTranslator_MapsDomainEvent_ToIntegrationEvent()
    {
        var domainEvent = new TrackRegisteredDomainEvent("t1", "One", 50, DateTime.UtcNow);

        IIntegrationEvent result = new TrackRegisteredToIntegrationEventTranslator().Translate(domainEvent);

        var ingested = Assert.IsType<TrackIngestedIntegrationEvent>(result);
        Assert.Equal("t1", ingested.TrackId);
        Assert.Equal("One", ingested.Name);
        Assert.Equal(50, ingested.Popularity);
        Assert.NotEqual(Guid.Empty, ingested.EventId);
        Assert.Equal(nameof(TrackIngestedIntegrationEvent), ingested.EventType);
    }

    [Fact]
    public void PlaylistTranslator_MapsDomainEvent_ToIntegrationEvent()
    {
        var domainEvent = new PlaylistIngestedDomainEvent("pl1", "Minha semente", 42, Now);

        IIntegrationEvent result = new PlaylistIngestedToIntegrationEventTranslator().Translate(domainEvent);

        var ingested = Assert.IsType<PlaylistIngestedIntegrationEvent>(result);
        Assert.Equal("pl1", ingested.PlaylistId);
        Assert.Equal("Minha semente", ingested.Name);
        Assert.Equal(42, ingested.TrackCount);
        Assert.Equal(Now, ingested.OccurredOnUtc);
        Assert.Equal(nameof(PlaylistIngestedIntegrationEvent), ingested.EventType);
    }
}
