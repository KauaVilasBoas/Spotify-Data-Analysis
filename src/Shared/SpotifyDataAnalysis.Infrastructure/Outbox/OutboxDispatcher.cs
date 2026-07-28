using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.SharedKernel.Messaging;
using SpotifyDataAnalysis.SharedKernel.Time;

namespace SpotifyDataAnalysis.Infrastructure.Outbox;

/// <summary>
/// Reads pending Outbox messages and publishes them via <see cref="IEventBus"/>.
/// Invoked by the background worker in SpotifyDataAnalysis.Jobs on a configurable interval.
///
/// Pending = <see cref="OutboxMessage.ProcessedAtUtc"/> is null AND the message has not been
/// dead-lettered (<see cref="OutboxMessage.DeadLetteredAtUtc"/> is null). After publishing, marks
/// <see cref="OutboxMessage.MarkProcessed"/> and saves. On failure, records the error via
/// <see cref="OutboxMessage.RecordError"/> (which increments the attempt count and dead-letters the
/// message once the limit is reached) and continues with the remaining messages — a failed message is
/// retried on the next run until it is dead-lettered, so one poison message never blocks the batch.
///
/// <para>Each module that participates in the Transactional Outbox owns its own <c>outbox_messages</c>
/// table (in its own schema, so the aggregate write and the outbox write share a single local
/// transaction). This dispatcher drains <b>every</b> registered <see cref="IOutboxDbContext"/> per tick —
/// injected as an <see cref="IEnumerable{T}"/> so N modules fan in without any per-module dispatcher or
/// ambiguous single-context registration.</para>
///
/// The host worker (SpotifyDataAnalysis.Jobs) is responsible for creating the DI scope and invoking
/// <see cref="ProcessPendingAsync"/>. This component is stateless and thread-safe.
/// </summary>
public sealed class OutboxDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IReadOnlyList<IOutboxDbContext> _outboxContexts;
    private readonly IEventBus _eventBus;
    private readonly IClock _clock;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IEnumerable<IOutboxDbContext> outboxContexts,
        IEventBus eventBus,
        IClock clock,
        ILogger<OutboxDispatcher> logger)
    {
        _outboxContexts = outboxContexts.ToList();
        _eventBus = eventBus;
        _clock = clock;
        _logger = logger;
    }

    /// <param name="batchSize">Maximum messages processed per outbox per invocation.</param>
    /// <param name="maxAttempts">
    /// Failed-delivery attempts a message tolerates before it is dead-lettered and dropped from the
    /// pending set.
    /// </param>
    /// <returns>Number of messages successfully published across every outbox in this run.</returns>
    public async Task<int> ProcessPendingAsync(
        int batchSize = 50,
        int maxAttempts = 5,
        CancellationToken cancellationToken = default)
    {
        int totalPublished = 0;

        // Each registered module outbox is drained independently: a failure or empty batch in one never
        // stops the others. Every context is a distinct DbContext with its own outbox_messages table.
        foreach (IOutboxDbContext outboxContext in _outboxContexts)
            totalPublished += await ProcessOutboxAsync(outboxContext, batchSize, maxAttempts, cancellationToken);

        return totalPublished;
    }

    private async Task<int> ProcessOutboxAsync(
        IOutboxDbContext outboxContext,
        int batchSize,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        List<OutboxMessage> pending = await outboxContext.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.DeadLetteredAtUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return 0;

        int published = 0;
        int failed = 0;
        int deadLettered = 0;

        foreach (OutboxMessage message in pending)
        {
            try
            {
                object? integrationEvent = DeserializeEvent(message);

                if (integrationEvent is null)
                {
                    _logger.LogWarning(
                        "Outbox: type '{EventType}' could not be deserialized. MessageId={MessageId}",
                        message.EventType, message.Id);

                    RecordFailure(message, $"Type not found: {message.EventType}", maxAttempts,
                        ref failed, ref deadLettered);
                    continue;
                }

                // Publish via EventBus using reflection to preserve the concrete type.
                await PublishDynamicAsync(integrationEvent, cancellationToken);

                message.MarkProcessed(_clock.UtcNow);
                published++;

                _logger.LogDebug(
                    "Outbox: published {EventType} | MessageId={MessageId}",
                    message.EventType, message.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Outbox: failed to publish {EventType} | MessageId={MessageId}",
                    message.EventType, message.Id);

                RecordFailure(message, ex.Message, maxAttempts, ref failed, ref deadLettered);
            }
        }

        // Persist ProcessedAtUtc / AttemptCount / DeadLetteredAtUtc / Error marks in the same context scope.
        await outboxContext.SaveChangesAsync(cancellationToken);

        // Derive the backlog signal from what we already know instead of a second COUNT round-trip: a full
        // batch means there may be more pending messages; a short batch means the pending set is drained.
        string backlog = pending.Count == batchSize ? "> 0 (batch full)" : "= 0";

        _logger.LogInformation(
            "Outbox tick: read {Read}, published {Published}, failed {Failed}, dead-lettered {DeadLettered}, backlog {Backlog}.",
            pending.Count, published, failed, deadLettered, backlog);

        return published;
    }

    /// <summary>
    /// Records a failed delivery on the message (increment + possible dead-letter) and updates the
    /// per-tick counters used for the structured tick log.
    /// </summary>
    private void RecordFailure(
        OutboxMessage message,
        string error,
        int maxAttempts,
        ref int failed,
        ref int deadLettered)
    {
        message.RecordError(error, maxAttempts, _clock);
        failed++;

        if (message.IsDeadLettered)
        {
            deadLettered++;
            _logger.LogWarning(
                "Outbox: message dead-lettered after {AttemptCount} attempts. MessageId={MessageId}, EventType={EventType}",
                message.AttemptCount, message.Id, message.EventType);
        }
    }

    private static object? DeserializeEvent(OutboxMessage message)
    {
        Type? eventType = Type.GetType(message.EventType);

        if (eventType is null)
            return null;

        return JsonSerializer.Deserialize(message.Payload, eventType, JsonOptions);
    }

    private async Task PublishDynamicAsync(object integrationEvent, CancellationToken cancellationToken)
    {
        // Resolve PublishAsync<TEvent> with the concrete event type via reflection.
        // Reflection cost here is irrelevant — this is a background batch job.
        var publishMethod = typeof(IEventBus)
            .GetMethod(nameof(IEventBus.PublishAsync))!
            .MakeGenericMethod(integrationEvent.GetType());

        await (Task)publishMethod.Invoke(_eventBus, [integrationEvent, cancellationToken])!;
    }
}
