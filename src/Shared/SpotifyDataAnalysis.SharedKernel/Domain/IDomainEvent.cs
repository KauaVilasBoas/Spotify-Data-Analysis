namespace SpotifyDataAnalysis.SharedKernel.Domain;

/// <summary>
/// Domain event marker. Domain events are internal to the bounded context and
/// describe something that happened in the domain (past tense, immutable).
/// </summary>
public interface IDomainEvent
{
    DateTime OccurredOnUtc { get; }
}
