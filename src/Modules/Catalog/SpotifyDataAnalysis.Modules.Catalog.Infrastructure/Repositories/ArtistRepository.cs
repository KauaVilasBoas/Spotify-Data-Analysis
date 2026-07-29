using Microsoft.EntityFrameworkCore;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Repositories;

/// <summary>
/// Implementação EF de <see cref="IArtistRepository"/> (write-side). <c>FindAsync</c> consulta primeiro o
/// change tracker — o que importa na ingestão, onde o mesmo artista reaparece em dezenas de faixas da mesma
/// playlist e só a primeira ocorrência vai ao banco.
/// </summary>
internal sealed class ArtistRepository : IArtistRepository
{
    private readonly CatalogDbContext _dbContext;

    public ArtistRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    public async Task<Artist?> GetByIdAsync(SpotifyArtistId id, CancellationToken cancellationToken = default)
        => await _dbContext.Artists.FindAsync([id], cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Artist>> ListPendingEnrichmentAsync(
        int limit, CancellationToken cancellationToken = default)
        // Ordena por id para que lotes sucessivos consumam a fila de pendentes de forma determinística, em vez
        // de depender da ordem física das linhas no PostgreSQL.
        => await _dbContext.Artists
            .Where(artist => !artist.IsEnriched)
            .OrderBy(artist => artist.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Artist artist, CancellationToken cancellationToken = default)
        => await _dbContext.Artists.AddAsync(artist, cancellationToken);
}
