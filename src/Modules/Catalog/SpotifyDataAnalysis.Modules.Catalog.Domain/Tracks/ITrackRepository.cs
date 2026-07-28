namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

/// <summary>
/// Repositório do agregado <see cref="Track"/> (interface no Domain, implementação EF na Infrastructure —
/// E1 persistence). Write-side: carrega e adiciona faixas; a mutação ocorre pelos métodos do agregado e é
/// persistida pelo <c>UnitOfWork</c>.
/// </summary>
public interface ITrackRepository
{
    Task<Track?> GetByIdAsync(SpotifyTrackId id, CancellationToken cancellationToken = default);

    Task AddAsync(Track track, CancellationToken cancellationToken = default);
}
