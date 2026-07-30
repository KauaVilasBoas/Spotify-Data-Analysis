using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Contracts.Events;

/// <summary>
/// Integration event público (fronteira do módulo Catalog): uma faixa foi ingerida no catálogo. Publicado
/// via Outbox para outros módulos (Analytics projeta read models; Prediction marca elegibilidade de treino).
/// Achatado e serializável — não vaza o domínio interno.
/// </summary>
public sealed record TrackIngestedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    string TrackId,
    string Name,
    int Popularity) : IIntegrationEvent
{
    public string EventType => nameof(TrackIngestedIntegrationEvent);
}
