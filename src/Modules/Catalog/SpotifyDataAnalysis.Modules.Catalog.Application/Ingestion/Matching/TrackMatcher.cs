using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Matching;

/// <summary>
/// Encadeia as <see cref="ITrackMatchingStrategy"/> na ordem em que foram registradas na DI — da mais
/// confiável (id exato) para a menos confiável (heurística textual) — e devolve o primeiro casamento
/// encontrado, junto do modo que o produziu (Chain of Responsibility).
///
/// A ordem é uma decisão de arquitetura registrada na composição do módulo, não uma regra escondida aqui:
/// acrescentar uma estratégia é acrescentar um registro, sem tocar neste tipo nem no handler.
/// </summary>
internal sealed class TrackMatcher
{
    private readonly IReadOnlyList<ITrackMatchingStrategy> _strategies;

    public TrackMatcher(IEnumerable<ITrackMatchingStrategy> strategies)
        => _strategies = strategies.ToList();

    public async Task<TrackMatch> MatchAsync(
        KaggleAudioFeaturesRow row, CancellationToken cancellationToken = default)
    {
        foreach (ITrackMatchingStrategy strategy in _strategies)
        {
            Track? track = await strategy.MatchAsync(row, cancellationToken);

            if (track is not null)
                return new TrackMatch(track, strategy.Kind);
        }

        return TrackMatch.NotFound;
    }
}
