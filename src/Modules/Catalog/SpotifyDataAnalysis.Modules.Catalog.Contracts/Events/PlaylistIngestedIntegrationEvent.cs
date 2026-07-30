using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Contracts.Events;

/// <summary>
/// Integration event público (fronteira do módulo Catalog): um ciclo de coleta de uma playlist-semente foi
/// concluído. Diferente do <see cref="TrackIngestedIntegrationEvent"/> (granular, uma faixa), este é o sinal
/// de <b>lote fechado</b> — o gancho natural para outros módulos reprojetarem read models ou reavaliarem a
/// elegibilidade de retreino uma única vez, em vez de a cada faixa.
/// </summary>
public sealed record PlaylistIngestedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    string PlaylistId,
    string Name,
    int TrackCount) : IIntegrationEvent
{
    public string EventType => nameof(PlaylistIngestedIntegrationEvent);
}
