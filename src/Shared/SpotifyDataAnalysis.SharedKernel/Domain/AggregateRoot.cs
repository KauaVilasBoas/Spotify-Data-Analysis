namespace SpotifyDataAnalysis.SharedKernel.Domain;

/// <summary>
/// Aggregate root. Extends <see cref="Entity{TId}"/> with an internal domain event collection.
/// Events are collected by infrastructure (UnitOfWork) after each SaveChanges,
/// dispatched, then cleared with <see cref="ClearDomainEvents"/>.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id) { }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
