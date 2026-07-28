namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;

/// <summary>
/// Repositório do agregado <see cref="Album"/> (interface no Domain, implementação EF na Infrastructure).
/// Write-side apenas: carrega e adiciona; a mutação ocorre pelos métodos do agregado.
/// </summary>
public interface IAlbumRepository
{
    Task<Album?> GetByIdAsync(SpotifyAlbumId id, CancellationToken cancellationToken = default);

    Task AddAsync(Album album, CancellationToken cancellationToken = default);
}
