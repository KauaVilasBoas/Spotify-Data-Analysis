using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Infrastructure.Messaging;

/// <summary>
/// In-process implementation of <see cref="IEventBus"/>.
/// Resolves integration event handlers from the DI container and invokes them in sequence.
///
/// Suitable for local development and architectures where all modules run in the same process.
/// For multi-instance production deployments, replace with a broker-based implementation
/// (RabbitMQ, SQS, etc.) — the <see cref="IEventBus"/> interface is the swap point.
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InMemoryEventBus> _logger;

    public InMemoryEventBus(IServiceProvider serviceProvider, ILogger<InMemoryEventBus> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Type handlerType = typeof(IIntegrationEventHandler<TEvent>);
        IEnumerable<object?> handlers = _serviceProvider.GetServices(handlerType);

        foreach (object? handler in handlers)
        {
            if (handler is not IIntegrationEventHandler<TEvent> typedHandler)
                continue;

            try
            {
                await typedHandler.HandleAsync(integrationEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "InMemoryEventBus: handler {HandlerType} failed for event {EventType}.",
                    handler.GetType().Name, typeof(TEvent).Name);

                // Do not rethrow — other handlers must still get the chance to run.
                // In production, consider Dead Letter Queue or compensation.
            }
        }
    }
}
