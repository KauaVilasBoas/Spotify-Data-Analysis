using Microsoft.EntityFrameworkCore;
using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Infrastructure.Persistence;

/// <summary>
/// EF Core-based implementation of <see cref="IUnitOfWork"/>.
/// Generic so each module instantiates it with its own derived DbContext.
///
/// SaveChangesAsync flow (hybrid consistency strategy):
/// 1. Collects all tracked aggregates with pending domain events.
/// 2. Dispatches TRANSACTIONAL handlers (ITransactionalDomainEventHandler) synchronously.
///    → Failure here rolls back the entire transaction (business invariant).
/// 3. Translates domain events to integration events and writes them to the Outbox
///    (in the same transaction) via IDomainEventDispatcher.
///    → Failure here also rolls back (the Outbox is part of local consistency).
/// 4. Calls SaveChangesAsync on the DbContext — persists everything atomically.
///
/// EVENTUAL dispatch (side effects via the Outbox) is handled outside this class,
/// by the background worker in SpotifyDataAnalysis.Jobs, which reads outbox_messages and publishes via
/// IEventBus.
/// </summary>
public sealed class EfUnitOfWork<TContext> : IUnitOfWork
    where TContext : DbContext
{
    private readonly TContext _dbContext;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public EfUnitOfWork(TContext dbContext, IDomainEventDispatcher domainEventDispatcher)
    {
        _dbContext = dbContext;
        _domainEventDispatcher = domainEventDispatcher;
    }

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Collect tracked aggregates that have pending domain events.
        List<IHasDomainEvents> aggregatesWithEvents = _dbContext.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        // Step 1: dispatch transactional handlers (in-transaction, rollback on failure).
        await _domainEventDispatcher.DispatchTransactionalAsync(aggregatesWithEvents, cancellationToken);

        // Step 2: translate domain events to integration events and write to the Outbox.
        //         Clears aggregate event lists after enqueueing.
        await _domainEventDispatcher.DispatchToOutboxAsync(aggregatesWithEvents, cancellationToken);

        // Step 3: persist everything (entities + outbox_messages) in a single EF transaction.
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
