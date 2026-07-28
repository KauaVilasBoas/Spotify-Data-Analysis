namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;

/// <summary>
/// Repositório do agregado <see cref="Artist"/> (interface no Domain, implementação EF na Infrastructure).
/// Write-side apenas: carrega e adiciona; a mutação ocorre pelos métodos do agregado e é persistida pelo
/// <c>UnitOfWork</c> na transação do command.
/// </summary>
public interface IArtistRepository
{
    Task<Artist?> GetByIdAsync(SpotifyArtistId id, CancellationToken cancellationToken = default);

    Task AddAsync(Artist artist, CancellationToken cancellationToken = default);
}
