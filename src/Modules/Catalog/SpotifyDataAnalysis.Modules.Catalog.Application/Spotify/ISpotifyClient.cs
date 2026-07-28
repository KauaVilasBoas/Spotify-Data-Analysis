namespace SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;

/// <summary>
/// Porta (abstração) do cliente da <b>Spotify Web API</b> — a fronteira do módulo Catalog com o serviço
/// externo. Vive na Application; a implementação (adapter HTTP) fica na Infrastructure. Os casos de uso de
/// ingestão (E1) dependem desta porta e mapeiam os DTOs retornados para os agregados de domínio.
///
/// Retorna <b>DTOs internos</b> (não o JSON cru, nem os agregados de Domain) — anti-corruption entre a API
/// externa e o domínio. A obtenção do token é responsabilidade de <see cref="ISpotifyTokenProvider"/>.
/// </summary>
public interface ISpotifyClient
{
    /// <summary>Faixas de uma playlist, paginadas (offset/limit da Spotify Web API).</summary>
    Task<SpotifyPlaylistTracksPage> GetPlaylistTracksAsync(
        string playlistId, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Percorre TODAS as faixas de uma playlist, paginando automaticamente até acabar. Preguiçoso
    /// (um yield por faixa, uma página por vez), sem materializar tudo em memória — base para a ingestão do E1.
    /// </summary>
    IAsyncEnumerable<SpotifyTrack> StreamPlaylistTracksAsync(
        string playlistId, int pageSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Uma faixa por id; <see langword="null"/> quando não existe (404).</summary>
    Task<SpotifyTrack?> GetTrackAsync(string trackId, CancellationToken cancellationToken = default);

    /// <summary>Um artista por id; <see langword="null"/> quando não existe (404).</summary>
    Task<SpotifyArtist?> GetArtistAsync(string artistId, CancellationToken cancellationToken = default);

    /// <summary>Um álbum por id; <see langword="null"/> quando não existe (404).</summary>
    Task<SpotifyAlbum?> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default);
}
