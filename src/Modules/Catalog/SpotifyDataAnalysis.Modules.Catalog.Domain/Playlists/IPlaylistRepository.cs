namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;

/// <summary>
/// Repositório do agregado <see cref="Playlist"/> (interface no Domain, implementação EF na Infrastructure).
/// Write-side apenas: carrega e adiciona; a mutação ocorre pelos métodos do agregado.
/// </summary>
public interface IPlaylistRepository
{
    Task<Playlist?> GetByIdAsync(SpotifyPlaylistId id, CancellationToken cancellationToken = default);

    Task AddAsync(Playlist playlist, CancellationToken cancellationToken = default);
}
