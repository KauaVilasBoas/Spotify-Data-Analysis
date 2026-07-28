using SpotifyDataAnalysis.Modules.Catalog.Contracts.Events;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks.Events;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Traduz o domain event interno <see cref="TrackRegisteredDomainEvent"/> no integration event público
/// <see cref="TrackIngestedIntegrationEvent"/> antes de gravar no Outbox. Registrado na DI do módulo; o
/// <c>DomainEventDispatcher</c> o resolve por tipo de evento durante o SaveChanges.
/// </summary>
public sealed class TrackRegisteredToIntegrationEventTranslator
    : IDomainEventToIntegrationEventTranslator<TrackRegisteredDomainEvent>
{
    public IIntegrationEvent Translate(TrackRegisteredDomainEvent domainEvent)
        => new TrackIngestedIntegrationEvent(
            EventId: Guid.NewGuid(),
            OccurredOnUtc: domainEvent.OccurredOnUtc,
            TrackId: domainEvent.TrackId,
            Name: domainEvent.Name,
            Popularity: domainEvent.Popularity);
}
