using Microsoft.EntityFrameworkCore;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Repositories;

/// <summary>
/// Implementação EF de <see cref="ITrackRepository"/> (write-side). A mutação ocorre pelos métodos do
/// agregado; o <c>UnitOfWork</c> coleta os domain events e persiste tudo na transação do command.
/// </summary>
internal sealed class TrackRepository : ITrackRepository
{
    private readonly CatalogDbContext _dbContext;

    public TrackRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    public async Task<Track?> GetByIdAsync(SpotifyTrackId id, CancellationToken cancellationToken = default)
        => await _dbContext.Tracks.FindAsync([id], cancellationToken);

    public async Task AddAsync(Track track, CancellationToken cancellationToken = default)
        => await _dbContext.Tracks.AddAsync(track, cancellationToken);
}
