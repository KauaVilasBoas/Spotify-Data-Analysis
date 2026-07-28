namespace SpotifyDataAnalysis.SharedKernel.Domain;

/// <summary>
/// Non-generic interface that exposes an aggregate's domain events.
/// Allows infrastructure (ChangeTracker, UnitOfWork) to collect events from any
/// <see cref="AggregateRoot{TId}"/> without knowing the identifier type.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
