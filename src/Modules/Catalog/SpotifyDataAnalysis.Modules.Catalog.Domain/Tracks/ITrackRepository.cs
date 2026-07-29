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

    /// <summary>
    /// Entre as faixas que colidem na mesma <paramref name="matchKey"/>, devolve a que melhor casa com a
    /// duração <paramref name="durationMs"/> — a de menor diferença absoluta, desde que dentro de
    /// <paramref name="toleranceMs"/>. É a desambiguação de homônimos do mesmo artista (E1.9): a chave textual
    /// sozinha os confunde, a duração da gravação os separa.
    ///
    /// Devolve <see langword="null"/> quando nenhuma candidata cai na tolerância — a estratégia então cede a
    /// vez ao fallback textual puro. Empates de diferença são resolvidos de forma determinística pelo id, para
    /// que reimportar o mesmo CSV leve sempre à mesma faixa.
    /// </summary>
    Task<Track?> FindByMatchKeyAndDurationAsync(
        TrackMatchKey matchKey, int durationMs, int toleranceMs,
        CancellationToken cancellationToken = default);

    Task AddAsync(Track track, CancellationToken cancellationToken = default);
}
