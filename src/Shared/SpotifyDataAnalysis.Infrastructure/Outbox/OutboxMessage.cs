using SpotifyDataAnalysis.SharedKernel.Guards;
using SpotifyDataAnalysis.SharedKernel.Time;

namespace SpotifyDataAnalysis.Infrastructure.Outbox;

/// <summary>
/// Record of a pending integration event, written in the same transaction as the originating command
/// (Transactional Outbox pattern).
///
/// Lifecycle:
/// 1. Written by <see cref="OutboxWriter"/> during SaveChangesAsync (pre-commit).
/// 2. Read by <see cref="OutboxDispatcher"/> (background) after commit.
/// 3. Published via <see cref="SpotifyDataAnalysis.SharedKernel.Messaging.IEventBus"/>.
/// 4. Marked as ProcessedAtUtc (idempotency: never reprocessed).
///
/// Delivery is retried on failure. Each failed attempt increments <see cref="AttemptCount"/>; once it
/// reaches the configured limit the message is dead-lettered (<see cref="DeadLetteredAtUtc"/> is set)
/// and the dispatcher stops retrying it, so a poison message never blocks or endlessly re-runs the loop.
///
/// Consumers should also be idempotent, using <see cref="Id"/> as the deduplication key.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Unique message id (= EventId of the IntegrationEvent).</summary>
    public Guid Id { get; private set; }

    /// <summary>Fully qualified type name of the event (for deserialization).</summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>JSON-serialized payload of the integration event.</summary>
    public string Payload { get; private set; } = string.Empty;

    public DateTime OccurredOnUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// When the message was successfully published. Null = not yet delivered.
    /// Processed messages are NOT reprocessed by the dispatcher.
    /// </summary>
    public DateTime? ProcessedAtUtc { get; private set; }

    /// <summary>
    /// Number of failed delivery attempts so far. Incremented by <see cref="RecordError"/>;
    /// drives the dead-letter decision.
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// When the message was dead-lettered (attempts exhausted). Null = still eligible for retry.
    /// A dead-lettered message is excluded from the pending set and never retried.
    /// </summary>
    public DateTime? DeadLetteredAtUtc { get; private set; }

    /// <summary>
    /// Error from the last failed delivery attempt.
    /// Preserved for diagnostics; does not by itself block retries.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>True once delivery has been given up on (attempts exhausted).</summary>
    public bool IsDeadLettered => DeadLetteredAtUtc is not null;

    // Private constructor for EF Core reconstitution.
    private OutboxMessage() { }

    public static OutboxMessage Create(
        Guid id,
        string eventType,
        string payload,
        DateTime occurredOnUtc,
        DateTime createdAtUtc)
    {
        return new OutboxMessage
        {
            Id = id,
            EventType = eventType,
            Payload = payload,
            OccurredOnUtc = occurredOnUtc,
            CreatedAtUtc = createdAtUtc,
            ProcessedAtUtc = null,
            AttemptCount = 0,
            DeadLetteredAtUtc = null,
            Error = null
        };
    }

    /// <summary>
    /// Marks the message as successfully published. Clears the last error and, being terminal,
    /// leaves the message out of the pending set forever.
    /// </summary>
    public void MarkProcessed(DateTime processedAtUtc)
    {
        ProcessedAtUtc = processedAtUtc;
        Error = null;
    }

    /// <summary>
    /// Records a failed delivery attempt: stores the error and increments <see cref="AttemptCount"/>.
    /// When the attempt count reaches <paramref name="maxAttempts"/>, the message is dead-lettered
    /// (see <see cref="DeadLetteredAtUtc"/>) so the dispatcher stops retrying it. Timestamps come from
    /// <paramref name="clock"/> to keep time deterministic and testable.
    /// </summary>
    /// <param name="error">Diagnostic message from the failed delivery.</param>
    /// <param name="maxAttempts">Attempt limit before dead-lettering (must be positive).</param>
    /// <param name="clock">Time source for the dead-letter timestamp.</param>
    public void RecordError(string error, int maxAttempts, IClock clock)
    {
        Guard.AgainstNonPositive(maxAttempts, nameof(maxAttempts));
        Guard.AgainstNull(clock, nameof(clock));

        Error = error;
        AttemptCount++;

        if (AttemptCount >= maxAttempts)
            MarkDeadLettered(clock.UtcNow);
    }

    private void MarkDeadLettered(DateTime deadLetteredAtUtc)
    {
        DeadLetteredAtUtc = deadLetteredAtUtc;
    }
}
