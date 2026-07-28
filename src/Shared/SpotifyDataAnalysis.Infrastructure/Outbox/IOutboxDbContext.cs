using Microsoft.EntityFrameworkCore;

namespace SpotifyDataAnalysis.Infrastructure.Outbox;

/// <summary>
/// Outbox DbSet accessor. Implemented by each
/// <see cref="SpotifyDataAnalysis.Infrastructure.Persistence.SpotifyDbContextBase"/> derived context that
/// participates in the Transactional Outbox Pattern.
///
/// To opt in, a module DbContext must:
/// 1. Implement this interface.
/// 2. Expose: public DbSet&lt;OutboxMessage&gt; OutboxMessages => Set&lt;OutboxMessage&gt;();
/// 3. Register <see cref="OutboxMessageConfiguration"/> in OnModelCreating.
///
/// The <see cref="SaveChangesAsync"/> member is part of the contract so the
/// <see cref="OutboxDispatcher"/> can persist its marks without an unsafe <c>is DbContext</c> cast:
/// a context that implements this interface but is not an EF <c>DbContext</c> would otherwise skip the
/// save silently and reprocess messages forever. Any EF-backed context satisfies it for free.
/// </summary>
public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }

    /// <summary>Persists the pending changes (e.g. the dispatcher's processed/dead-letter marks).</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
