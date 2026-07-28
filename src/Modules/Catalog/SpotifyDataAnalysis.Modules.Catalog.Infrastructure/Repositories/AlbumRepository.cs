using SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Repositories;

/// <summary>Implementação EF de <see cref="IAlbumRepository"/> (write-side).</summary>
internal sealed class AlbumRepository : IAlbumRepository
{
    private readonly CatalogDbContext _dbContext;

    public AlbumRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    public async Task<Album?> GetByIdAsync(SpotifyAlbumId id, CancellationToken cancellationToken = default)
        => await _dbContext.Albums.FindAsync([id], cancellationToken);

    public async Task AddAsync(Album album, CancellationToken cancellationToken = default)
        => await _dbContext.Albums.AddAsync(album, cancellationToken);
}
