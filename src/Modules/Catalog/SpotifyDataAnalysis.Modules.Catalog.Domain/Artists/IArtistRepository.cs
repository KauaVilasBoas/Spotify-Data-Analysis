namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;

/// <summary>
/// Repositório do agregado <see cref="Artist"/> (interface no Domain, implementação EF na Infrastructure).
/// Write-side apenas: carrega e adiciona; a mutação ocorre pelos métodos do agregado e é persistida pelo
/// <c>UnitOfWork</c> na transação do command.
/// </summary>
public interface IArtistRepository
{
    Task<Artist?> GetByIdAsync(SpotifyArtistId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Artistas que ainda não tiveram o perfil carregado da API (<see cref="Artist.IsEnriched"/> falso), até
    /// <paramref name="limit"/> por vez — o trabalho pendente do enriquecimento (E1.8). Processar em lotes
    /// pequenos mantém a transação do command curta em vez de abrir uma única gigante sobre todo o catálogo.
    /// </summary>
    Task<IReadOnlyList<Artist>> ListPendingEnrichmentAsync(
        int limit, CancellationToken cancellationToken = default);

    Task AddAsync(Artist artist, CancellationToken cancellationToken = default);
}
