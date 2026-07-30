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

    /// <inheritdoc />
    public async Task<Track?> FindByMatchKeyAsync(
        TrackMatchKey matchKey, CancellationToken cancellationToken = default)
        // A chave não é única: ordenar por id torna a escolha determinística — reimportar o mesmo CSV leva
        // sempre à mesma faixa, em vez de depender da ordem física das linhas no PostgreSQL.
        => await _dbContext.Tracks
            .Where(track => track.MatchKey == matchKey)
            .OrderBy(track => track.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Track?> FindByMatchKeyAndDurationAsync(
        TrackMatchKey matchKey, int durationMs, int toleranceMs, CancellationToken cancellationToken = default)
        // Filtra as homônimas pela janela de tolerância e escolhe a de menor diferença de duração; o id como
        // segundo critério desempata determinística e traduzivelmente (Npgsql traduz abs() e o ORDER BY),
        // usando o índice ix_tracks_match_key para não varrer a tabela.
        => await _dbContext.Tracks
            .Where(track => track.MatchKey == matchKey
                && Math.Abs(track.DurationMs - durationMs) <= toleranceMs)
            .OrderBy(track => Math.Abs(track.DurationMs - durationMs))
            .ThenBy(track => track.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(Track track, CancellationToken cancellationToken = default)
        => await _dbContext.Tracks.AddAsync(track, cancellationToken);
}
