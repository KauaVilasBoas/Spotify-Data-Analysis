using SpotifyDataAnalysis.Modules.Catalog.Contracts.Events;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists.Events;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Traduz o domain event interno <see cref="PlaylistIngestedDomainEvent"/> no integration event público
/// <see cref="PlaylistIngestedIntegrationEvent"/> antes de gravar no Outbox. Registrado na DI do módulo; o
/// <c>DomainEventDispatcher</c> o resolve por tipo de evento durante o SaveChanges.
/// </summary>
public sealed class PlaylistIngestedToIntegrationEventTranslator
    : IDomainEventToIntegrationEventTranslator<PlaylistIngestedDomainEvent>
{
    public IIntegrationEvent Translate(PlaylistIngestedDomainEvent domainEvent)
        => new PlaylistIngestedIntegrationEvent(
            EventId: Guid.NewGuid(),
            OccurredOnUtc: domainEvent.OccurredOnUtc,
            PlaylistId: domainEvent.PlaylistId,
            Name: domainEvent.Name,
            TrackCount: domainEvent.TrackCount);
}
