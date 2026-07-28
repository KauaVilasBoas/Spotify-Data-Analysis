namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>
/// Integration event marker — a public, flattened representation of a DomainEvent,
/// published to other bounded contexts via the Outbox/event bus.
///
/// Unlike a DomainEvent (module-internal, rich, discarded after dispatch), an IntegrationEvent
/// is serialized as JSON in the Outbox and transportable via a queue/broker.
///
/// Convention: each DomainEvent that needs to cross bounded-context boundaries must have a
/// corresponding IntegrationEvent in the emitting module's Contracts project.
/// Translation (DomainEvent → IntegrationEvent) happens in the module's event handler
/// before writing to the Outbox.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Unique event identifier (used for consumer-side idempotency).</summary>
    Guid EventId { get; }

    DateTime OccurredOnUtc { get; }

    /// <summary>Event type name — discriminator for polymorphic deserialization.</summary>
    string EventType { get; }
}
