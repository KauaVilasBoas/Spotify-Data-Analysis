using SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Common;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.SharedKernel.Time;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Fakes;

/// <summary>
/// Repositórios em memória dos agregados do Catalog. Substituem o EF nos testes de caso de uso: o
/// comportamento que importa (buscar por id, adicionar, e a mutação acontecer no próprio agregado
/// rastreado) é idêntico, sem banco nem migrations.
/// </summary>
internal sealed class InMemoryTrackRepository : ITrackRepository
{
    private readonly Dictionary<string, Track> _store = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Track> Store => _store;

    public void Seed(Track track) => _store[track.Id.Value] = track;

    public Task<Track?> GetByIdAsync(SpotifyTrackId id, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.GetValueOrDefault(id.Value));

    public Task<Track?> FindByMatchKeyAsync(
        TrackMatchKey matchKey, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.Values
            .Where(track => track.MatchKey == matchKey)
            .OrderBy(track => track.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault());

    public Task<Track?> FindByMatchKeyAndDurationAsync(
        TrackMatchKey matchKey, int durationMs, int toleranceMs,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_store.Values
            .Where(track => track.MatchKey == matchKey
                && Math.Abs(track.DurationMs - durationMs) <= toleranceMs)
            .OrderBy(track => Math.Abs(track.DurationMs - durationMs))
            .ThenBy(track => track.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault());

    public Task AddAsync(Track track, CancellationToken cancellationToken = default)
    {
        _store[track.Id.Value] = track;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryArtistRepository : IArtistRepository
{
    private readonly Dictionary<string, Artist> _store = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Artist> Store => _store;

    public Task<Artist?> GetByIdAsync(SpotifyArtistId id, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.GetValueOrDefault(id.Value));

    public Task<IReadOnlyList<Artist>> ListPendingEnrichmentAsync(
        int limit, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Artist>>(_store.Values
            .Where(artist => !artist.IsEnriched)
            .OrderBy(artist => artist.Id.Value, StringComparer.Ordinal)
            .Take(limit)
            .ToList());

    public Task AddAsync(Artist artist, CancellationToken cancellationToken = default)
    {
        _store[artist.Id.Value] = artist;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryAlbumRepository : IAlbumRepository
{
    private readonly Dictionary<string, Album> _store = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Album> Store => _store;

    public Task<Album?> GetByIdAsync(SpotifyAlbumId id, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.GetValueOrDefault(id.Value));

    public Task<IReadOnlyList<Album>> ListPendingEnrichmentAsync(
        int limit, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Album>>(_store.Values
            .Where(album => !album.IsEnriched)
            .OrderBy(album => album.Id.Value, StringComparer.Ordinal)
            .Take(limit)
            .ToList());

    public Task AddAsync(Album album, CancellationToken cancellationToken = default)
    {
        _store[album.Id.Value] = album;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryPlaylistRepository : IPlaylistRepository
{
    private readonly Dictionary<string, Playlist> _store = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Playlist> Store => _store;

    public void Seed(Playlist playlist) => _store[playlist.Id.Value] = playlist;

    public Task<Playlist?> GetByIdAsync(SpotifyPlaylistId id, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.GetValueOrDefault(id.Value));

    public Task AddAsync(Playlist playlist, CancellationToken cancellationToken = default)
    {
        _store[playlist.Id.Value] = playlist;
        return Task.CompletedTask;
    }
}

/// <summary>Relógio determinístico — o teste decide "agora".</summary>
internal sealed class FixedClock : IClock
{
    public FixedClock(DateTime utcNow) => UtcNow = utcNow;

    public DateTime UtcNow { get; }
}

/// <summary>Atalhos para montar agregados válidos nos testes sem repetir os parâmetros irrelevantes.</summary>
internal static class CatalogFixtures
{
    public static Track Track(
        string id, string name = "Song", int popularity = 50, TrackArtist? artist = null, Isrc? isrc = null,
        int durationMs = 200_000)
        => global::SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks.Track.Register(
            SpotifyTrackId.Of(id), name, Popularity.Of(popularity), durationMs,
            @explicit: false, albumId: null, artists: artist is null ? [] : [artist], isrc: isrc);

    public static TrackArtist Artist(string id = "artist1", string name = "Queen")
        => TrackArtist.Of(id, name);
}
