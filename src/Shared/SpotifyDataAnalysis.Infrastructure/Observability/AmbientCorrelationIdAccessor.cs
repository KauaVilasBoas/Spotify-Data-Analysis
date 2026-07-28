using SpotifyDataAnalysis.SharedKernel.Observability;

namespace SpotifyDataAnalysis.Infrastructure.Observability;

/// <summary>
/// Default <see cref="ICorrelationIdAccessor"/> for consumers that run outside the HTTP pipeline —
/// background jobs, the Outbox dispatcher, and unit tests.
///
/// It mints a stable id per scope (per job iteration / per resolution) so the CQRS
/// <c>LoggingBehavior</c> always has a non-empty correlation id even when no <c>X-Correlation-Id</c>
/// header exists. The Host replaces this registration with its request-aware accessor (populated by
/// <c>CorrelationIdMiddleware</c>); this one is the safe fallback everywhere else.
///
/// Registered as Scoped so each unit of work observes a single, consistent id.
/// </summary>
public sealed class AmbientCorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <inheritdoc />
    public string CorrelationId { get; } = Guid.NewGuid().ToString("N");
}
