using SpotifyDataAnalysis.SharedKernel.Domain;

namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>
/// Translates an <see cref="IDomainEvent"/> (internal, rich) into an <see cref="IIntegrationEvent"/>
/// (public, flattened) before writing to the Outbox.
///
/// Each DomainEvent that needs to cross bounded-context boundaries must have a concrete translator
/// registered in the DI container. <see cref="IDomainEventDispatcher"/> consults this translator
/// before enqueueing the message in the Outbox.
///
/// Implement in the emitting module's Application project. The resulting IntegrationEvent
/// should be defined in the same module's Contracts project.
/// </summary>
public interface IDomainEventToIntegrationEventTranslator<in TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    IIntegrationEvent Translate(TDomainEvent domainEvent);
}
