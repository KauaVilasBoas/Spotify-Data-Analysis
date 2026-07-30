using System.Runtime.CompilerServices;
using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Fakes;

/// <summary>
/// Cliente Spotify fake para os testes de ingestão: devolve uma playlist e uma sequência fixa de faixas,
/// sem rede. Os endpoints que a ingestão não usa lançam <see cref="NotSupportedException"/> de propósito —
/// se um deles for chamado, é regressão de escopo do handler e o teste deve falhar alto.
/// </summary>
internal sealed class FakeSpotifyClient : ISpotifyClient
{
    private readonly IReadOnlyList<SpotifyTrack> _tracks;

    public FakeSpotifyClient(params SpotifyTrack[] tracks) => _tracks = tracks;

    /// <summary>Metadados devolvidos por <see cref="GetPlaylistAsync"/>; nulo simula uma playlist inexistente.</summary>
    public SpotifyPlaylist? Playlist { get; init; } =
        new("pl1", "Minha semente", "kaua", TotalTracks: 0);

    public Task<SpotifyPlaylist?> GetPlaylistAsync(
        string playlistId, CancellationToken cancellationToken = default)
        => Task.FromResult(Playlist);

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

    public Task<IReadOnlyList<SpotifyArtist>> GetArtistsAsync(
        IReadOnlyCollection<string> artistIds, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<SpotifyAlbum>> GetAlbumsAsync(
        IReadOnlyCollection<string> albumIds, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
