using System.Text.Json;
using SpotifyDataAnalysis.SharedKernel.Messaging;
using SpotifyDataAnalysis.SharedKernel.Time;

namespace SpotifyDataAnalysis.Infrastructure.Outbox;

/// <summary>
/// Serializes integration events and writes them to the Outbox inside the current transaction.
/// Injected into <see cref="SpotifyDataAnalysis.Infrastructure.Persistence.EfUnitOfWork{TContext}"/>.
///
/// The concrete DbContext is typed as <see cref="IOutboxDbContext"/> so the writer can
/// access the OutboxMessage DbSet without depending on a module-specific context.
/// </summary>
public sealed class OutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IOutboxDbContext _outboxContext;
    private readonly IClock _clock;

    public OutboxWriter(IOutboxDbContext outboxContext, IClock clock)
    {
        _outboxContext = outboxContext;
        _clock = clock;
    }

    /// <summary>
    /// Serializes and enqueues an integration event into the Outbox.
    /// Must be called BEFORE SaveChanges so the write is part of the same transaction.
    /// </summary>
    public void Enqueue<TEvent>(TEvent integrationEvent)
        where TEvent : class, IIntegrationEvent
    {
        string payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), JsonOptions);

        OutboxMessage message = OutboxMessage.Create(
            id: integrationEvent.EventId,
            eventType: integrationEvent.GetType().AssemblyQualifiedName
                       ?? integrationEvent.GetType().FullName
                       ?? integrationEvent.EventType,
            payload: payload,
            occurredOnUtc: integrationEvent.OccurredOnUtc,
            createdAtUtc: _clock.UtcNow);

        _outboxContext.OutboxMessages.Add(message);
    }
}
