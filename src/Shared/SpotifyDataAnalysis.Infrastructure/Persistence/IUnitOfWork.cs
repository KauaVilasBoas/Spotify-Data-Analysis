namespace SpotifyDataAnalysis.Infrastructure.Persistence;

/// <summary>
/// Unit of work: encapsulates a module's EF Core transaction.
/// The concrete implementation (<see cref="EfUnitOfWork{TContext}"/>) dispatches domain events,
/// writes Outbox messages, then calls SaveChangesAsync — all in one atomic operation.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
