using SpotifyDataAnalysis.SharedKernel.Domain;

namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>
/// Domain event handler.
///
/// Consistency strategy:
/// - Handlers implementing <see cref="ITransactionalDomainEventHandler{TEvent}"/> run synchronously
///   INSIDE the transaction (pre-SaveChanges). Failure = full rollback. Use ONLY for business invariants.
/// - Handlers implementing only this interface run via the Outbox, eventually (post-commit).
///   The main operation is NOT affected by failures here. Use for side effects (notifications,
///   cross-module sync, external calls).
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker: this handler runs synchronously INSIDE the current transaction, before SaveChanges.
/// Any exception thrown here causes a full rollback. The handler MAY modify the DbContext —
/// its changes are included in the same SaveChanges. Not suitable for external calls (HTTP, queues)
/// — use the Outbox for those.
/// </summary>
public interface ITransactionalDomainEventHandler<in TEvent> : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent
{
}
