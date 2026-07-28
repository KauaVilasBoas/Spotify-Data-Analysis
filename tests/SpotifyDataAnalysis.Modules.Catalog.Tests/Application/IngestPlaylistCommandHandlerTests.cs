using System.Runtime.CompilerServices;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.Modules.Catalog.Contracts.Events;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks.Events;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Application;

/// <summary>
/// Testes do <see cref="IngestPlaylistCommandHandler"/> (ingestão idempotente por SpotifyTrackId) e do
/// translator DomainEvent → IntegrationEvent, com um cliente Spotify e um repositório fakes (sem rede/banco).
/// </summary>
public sealed class IngestPlaylistCommandHandlerTests
{
    private sealed class FakeSpotifyClient : ISpotifyClient
    {
        private readonly IReadOnlyList<SpotifyTrack> _tracks;

        public FakeSpotifyClient(params SpotifyTrack[] tracks) => _tracks = tracks;

        public async IAsyncEnumerable<SpotifyTrack> StreamPlaylistTracksAsync(
            string playlistId, int pageSize = 100,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            foreach (SpotifyTrack track in _tracks)
                yield return track;
        }

        public Task<SpotifyPlaylistTracksPage> GetPlaylistTracksAsync(
            string playlistId, int offset, int limit, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SpotifyTrack?> GetTrackAsync(string trackId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SpotifyArtist?> GetArtistAsync(string artistId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SpotifyAlbum?> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class InMemoryTrackRepository : ITrackRepository
    {
        private readonly Dictionary<string, Track> _store = new();

        public IReadOnlyDictionary<string, Track> Store => _store;

        public Task<Track?> GetByIdAsync(SpotifyTrackId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.TryGetValue(id.Value, out Track? track) ? track : null);

        public Task AddAsync(Track track, CancellationToken cancellationToken = default)
        {
            _store[track.Id.Value] = track;
            return Task.CompletedTask;
        }
    }

    private static SpotifyTrack Dto(string id, string name, int popularity = 50)
        => new(id, name, popularity, DurationMs: 1000, Explicit: false, Artists: [], Album: null);

    [Fact]
    public async Task Ingests_NewTracks()
    {
        var client = new FakeSpotifyClient(Dto("t1", "One"), Dto("t2", "Two"));
        var repo = new InMemoryTrackRepository();
        var handler = new IngestPlaylistCommandHandler(client, repo);

        IngestPlaylistResult result = await handler.HandleAsync(new IngestPlaylistCommand("pl1"));

        Assert.Equal(2, result.Ingested);
        Assert.Equal(0, result.Updated);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, repo.Store.Count);
    }

    [Fact]
    public async Task Updates_ExistingTrackPopularity()
    {
        var repo = new InMemoryTrackRepository();
        await repo.AddAsync(Track.Register(
            SpotifyTrackId.Of("t1"), "One", Popularity.Of(10), 1000, false, null, Array.Empty<string>()));

        var client = new FakeSpotifyClient(Dto("t1", "One", popularity: 90));
        var handler = new IngestPlaylistCommandHandler(client, repo);

        IngestPlaylistResult result = await handler.HandleAsync(new IngestPlaylistCommand("pl1"));

        Assert.Equal(0, result.Ingested);
        Assert.Equal(1, result.Updated);
        Assert.Equal(90, repo.Store["t1"].Popularity.Value);
    }

    [Fact]
    public async Task Skips_TracksWithoutIdOrName()
    {
        var client = new FakeSpotifyClient(Dto("", "Local file"), Dto("t9", "   "), Dto("t1", "One"));
        var repo = new InMemoryTrackRepository();
        var handler = new IngestPlaylistCommandHandler(client, repo);

        IngestPlaylistResult result = await handler.HandleAsync(new IngestPlaylistCommand("pl1"));

        Assert.Equal(1, result.Ingested);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public void Translator_MapsDomainEvent_ToIntegrationEvent()
    {
        var domainEvent = new TrackRegisteredDomainEvent("t1", "One", 50, DateTime.UtcNow);
        var translator = new TrackRegisteredToIntegrationEventTranslator();

        IIntegrationEvent result = translator.Translate(domainEvent);

        var ingested = Assert.IsType<TrackIngestedIntegrationEvent>(result);
        Assert.Equal("t1", ingested.TrackId);
        Assert.Equal("One", ingested.Name);
        Assert.Equal(50, ingested.Popularity);
        Assert.NotEqual(Guid.Empty, ingested.EventId);
        Assert.Equal(nameof(TrackIngestedIntegrationEvent), ingested.EventType);
    }
}
