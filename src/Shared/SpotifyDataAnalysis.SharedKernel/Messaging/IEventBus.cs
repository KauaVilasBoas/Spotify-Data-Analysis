namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>
/// Integration event bus. The concrete implementation decides the delivery mechanism
/// (in-process, Outbox + broker, etc.).
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class;
}
