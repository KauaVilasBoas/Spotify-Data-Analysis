using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Repositories;

/// <summary>Implementação EF de <see cref="IPlaylistRepository"/> (write-side).</summary>
internal sealed class PlaylistRepository : IPlaylistRepository
{
    private readonly CatalogDbContext _dbContext;

    public PlaylistRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    public async Task<Playlist?> GetByIdAsync(
        SpotifyPlaylistId id, CancellationToken cancellationToken = default)
        => await _dbContext.Playlists.FindAsync([id], cancellationToken);

    public async Task AddAsync(Playlist playlist, CancellationToken cancellationToken = default)
        => await _dbContext.Playlists.AddAsync(playlist, cancellationToken);
}
