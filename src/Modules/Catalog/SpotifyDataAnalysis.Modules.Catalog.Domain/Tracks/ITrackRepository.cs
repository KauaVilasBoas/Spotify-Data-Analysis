namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

/// <summary>
/// Repositório do agregado <see cref="Track"/> (interface no Domain, implementação EF na Infrastructure —
/// E1 persistence). Write-side: carrega e adiciona faixas; a mutação ocorre pelos métodos do agregado e é
/// persistida pelo <c>UnitOfWork</c>.
/// </summary>
public interface ITrackRepository
{
    Task<Track?> GetByIdAsync(SpotifyTrackId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca uma faixa pela chave normalizada "artista + título" — o fallback de casamento com o dataset
    /// externo (E1.4). A chave <b>não é única</b> (faixas homônimas do mesmo artista colidem), então a
    /// implementação devolve a primeira ocorrência de forma determinística.
    /// </summary>
    Task<Track?> FindByMatchKeyAsync(TrackMatchKey matchKey, CancellationToken cancellationToken = default);

    Task AddAsync(Track track, CancellationToken cancellationToken = default);
}
