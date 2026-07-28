namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>
/// Integration event handler. Consumes events published via <see cref="IEventBus"/>,
/// originating from the Outbox of this or another module.
///
/// Handlers must be idempotent — the same event may be delivered more than once
/// (Outbox reprocessing after failure). Use the integration event's EventId as a
/// deduplication key when necessary.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
