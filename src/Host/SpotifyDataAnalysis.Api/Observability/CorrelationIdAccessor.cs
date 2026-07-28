using SpotifyDataAnalysis.SharedKernel.Observability;

namespace SpotifyDataAnalysis.Api.Observability;

/// <summary>
/// Scoped, mutable holder for the current request's correlation id.
///
/// One instance exists per HTTP request. <see cref="Middleware.CorrelationIdMiddleware"/> sets the resolved
/// id early in the pipeline via <see cref="Set"/>; every downstream consumer reads it through the read-only
/// <see cref="ICorrelationIdAccessor"/> contract. The setter is kept off the public interface so handlers
/// and behaviors can only observe the id, never overwrite it.
///
/// Before the middleware runs (or outside a request scope), the accessor returns a freshly generated
/// fallback id so consumers never see <see langword="null"/>.
/// </summary>
internal sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private string _correlationId = Guid.NewGuid().ToString("N");

    /// <inheritdoc />
    public string CorrelationId => _correlationId;

    /// <summary>Assigns the correlation id resolved for the current request. Called once by the middleware.</summary>
    public void Set(string correlationId) => _correlationId = correlationId;
}
