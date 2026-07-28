using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.Infrastructure.Outbox;
using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Infrastructure.Messaging;

/// <summary>
/// <see cref="IDomainEventDispatcher"/> implementation.
///
/// Flow within SaveChangesAsync (orchestrated by EfUnitOfWork):
///
/// 1. <see cref="DispatchTransactionalAsync"/>: for each DomainEvent that has at least one
///    <see cref="ITransactionalDomainEventHandler{TEvent}"/> registered in the DI container,
///    invokes all handlers inside the current transaction. Failure = rollback.
///
/// 2. <see cref="DispatchToOutboxAsync"/>: remaining events (or events that also need eventual
///    side effects) are translated to IntegrationEvents and written to the Outbox via
///    <see cref="OutboxWriter"/>. Clears the aggregate event lists.
///
/// A DomainEvent can have BOTH a transactional handler AND be enqueued in the Outbox.
/// The transactional handler runs first (in-transaction invariant); the Outbox handles
/// eventual side effects.
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly OutboxWriter _outboxWriter;
    private readonly ILogger<DomainEventDispatcher> _logger;

    public DomainEventDispatcher(
        IServiceProvider serviceProvider,
        OutboxWriter outboxWriter,
        ILogger<DomainEventDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _outboxWriter = outboxWriter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task DispatchTransactionalAsync(
        IEnumerable<IHasDomainEvents> aggregates,
        CancellationToken cancellationToken = default)
    {
        List<IDomainEvent> allEvents = aggregates
            .SelectMany(a => a.DomainEvents)
            .ToList();

        foreach (IDomainEvent domainEvent in allEvents)
        {
            await DispatchTransactionalHandlersForEvent(domainEvent, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task DispatchToOutboxAsync(
        IEnumerable<IHasDomainEvents> aggregates,
        CancellationToken cancellationToken = default)
    {
        List<IHasDomainEvents> aggregateList = aggregates.ToList();
        List<IDomainEvent> allEvents = aggregateList
            .SelectMany(a => a.DomainEvents)
            .ToList();

        foreach (IDomainEvent domainEvent in allEvents)
        {
            await EnqueueEventInOutbox(domainEvent, cancellationToken);
        }

        // Clear events after Outbox write — they are now safely persisted in outbox_messages.
        foreach (IHasDomainEvents aggregate in aggregateList)
            aggregate.ClearDomainEvents();
    }

    private async Task DispatchTransactionalHandlersForEvent(
        IDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        Type eventType = domainEvent.GetType();
        Type handlerInterface = typeof(ITransactionalDomainEventHandler<>).MakeGenericType(eventType);

        IEnumerable<object?> handlers = _serviceProvider.GetServices(handlerInterface);

        foreach (object? handler in handlers)
        {
            if (handler is null) continue;

            _logger.LogDebug(
                "DomainEventDispatcher: running transactional handler {Handler} for {Event}",
                handler.GetType().Name, eventType.Name);

            // Reflection invocation — acceptable cost for domain events (low volume).
            // HandleAsync is declared in the BASE IDomainEventHandler<> interface; GetMethod on
            // a derived interface does not return inherited members, so resolve on the base.
            Type baseHandlerInterface = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var handleMethod = baseHandlerInterface.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
            await (Task)handleMethod.Invoke(handler, [domainEvent, cancellationToken])!;
        }
    }

    private Task EnqueueEventInOutbox(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        // Check whether a translator is registered for this DomainEvent type.
        // The translator converts DomainEvent → IIntegrationEvent before writing to the Outbox.
        Type eventType = domainEvent.GetType();
        Type translatorInterface = typeof(IDomainEventToIntegrationEventTranslator<>).MakeGenericType(eventType);

        object? translator = _serviceProvider.GetService(translatorInterface);

        if (translator is null)
        {
            _logger.LogDebug(
                "DomainEventDispatcher: no translator for {EventType} — event not written to Outbox.",
                eventType.Name);

            return Task.CompletedTask;
        }

        var translateMethod = translatorInterface.GetMethod(
            nameof(IDomainEventToIntegrationEventTranslator<IDomainEvent>.Translate))!;

        IIntegrationEvent integrationEvent = (IIntegrationEvent)translateMethod.Invoke(translator, [domainEvent])!;

        _outboxWriter.Enqueue(integrationEvent);

        _logger.LogDebug(
            "DomainEventDispatcher: {EventType} translated and enqueued in Outbox as {IntegrationEventType}.",
            eventType.Name, integrationEvent.GetType().Name);

        return Task.CompletedTask;
    }
}
