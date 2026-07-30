using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Matching;

/// <summary>
/// Uma forma de localizar no catálogo a faixa correspondente a uma linha do dataset externo (Strategy).
///
/// As estratégias são encadeadas da mais confiável para a menos confiável pelo <see cref="TrackMatcher"/>
/// (Chain of Responsibility): a primeira que encontrar vence. Uma futura terceira via — casar por ISRC, por
/// duração aproximada — entra como mais uma implementação, sem tocar no handler de importação.
/// </summary>
internal interface ITrackMatchingStrategy
{
    /// <summary>Como este casamento deve ser reportado na métrica de qualidade da ingestão.</summary>
    TrackMatchKind Kind { get; }

    /// <summary>Localiza a faixa; <see langword="null"/> quando esta estratégia não sabe resolver a linha.</summary>
    Task<Track?> MatchAsync(KaggleAudioFeaturesRow row, CancellationToken cancellationToken = default);
}
