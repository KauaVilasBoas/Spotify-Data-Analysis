using Microsoft.EntityFrameworkCore;
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<Album>> ListPendingEnrichmentAsync(
        int limit, CancellationToken cancellationToken = default)
        // Mesma semântica anti-starvation de ArtistRepository: exclui enriquecidos e os que esgotaram as
        // tentativas, ordena por tentativas ASC e desempata por id, para a fila progredir mesmo com um prefixo
        // de ids irresolúveis.
        => await _dbContext.Albums
            .Where(album => !album.IsEnriched && album.EnrichmentAttempts < Album.MaxEnrichmentAttempts)
            .OrderBy(album => album.EnrichmentAttempts)
            .ThenBy(album => album.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Album album, CancellationToken cancellationToken = default)
        => await _dbContext.Albums.AddAsync(album, cancellationToken);
}
