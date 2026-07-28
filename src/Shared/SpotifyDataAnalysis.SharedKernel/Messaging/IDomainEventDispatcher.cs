using SpotifyDataAnalysis.SharedKernel.Domain;

namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>
/// Dispatches domain events collected from aggregates according to the hybrid consistency strategy:
///
/// - <see cref="ITransactionalDomainEventHandler{TEvent}"/>: runs synchronously BEFORE SaveChanges,
///   inside the same transaction. Failure = full rollback.
///
/// - <see cref="IDomainEventHandler{TEvent}"/> (non-transactional): translated to IntegrationEvents
///   and written to the Outbox in the same transaction. Publication is eventual (post-commit).
///
/// Called by infrastructure (EfUnitOfWork) during SaveChangesAsync.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches transactional handlers for all pending events in the provided aggregates.
    /// Must be called BEFORE SaveChanges. Does not clear events — waits for SaveChanges confirmation.
    /// </summary>
    Task DispatchTransactionalAsync(
        IEnumerable<IHasDomainEvents> aggregates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Translates the aggregates' domain events to IntegrationEvents and writes them to
    /// the Outbox (via the UoW/DbContext) still inside the same transaction.
    /// Clears the aggregates' event lists after Outbox write.
    /// Must be called AFTER transactional dispatch and BEFORE SaveChanges.
    /// </summary>
    Task DispatchToOutboxAsync(
        IEnumerable<IHasDomainEvents> aggregates,
        CancellationToken cancellationToken = default);
}
