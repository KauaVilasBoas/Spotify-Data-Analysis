namespace SpotifyDataAnalysis.SharedKernel.Observability;

/// <summary>
/// Ambient accessor for the correlation id of the current request.
///
/// The value is a stable identifier that ties together every log line, downstream call and error
/// response produced while handling a single request. It is resolved once per request by the
/// <c>CorrelationIdMiddleware</c> in the Host — from the inbound <c>X-Correlation-Id</c> header when the
/// caller provides one, or a freshly generated id otherwise — and echoed back on the response so the
/// client (and any log aggregator) can join both sides of the conversation.
///
/// Lives in the pure <c>SharedKernel</c> as an abstraction so cross-cutting consumers — the CQRS
/// <c>LoggingBehavior</c>, the RFC 7807 <c>traceId</c> — depend on the contract, never on the ASP.NET
/// implementation. Register the implementation as <b>Scoped</b> (one id per HTTP request).
/// </summary>
public interface ICorrelationIdAccessor
{
    /// <summary>
    /// Correlation id for the current request. Never <see langword="null"/> or empty once the middleware
    /// has run; consumers resolved outside a request scope (e.g. background work) get a fallback id.
    /// </summary>
    string CorrelationId { get; }
}
