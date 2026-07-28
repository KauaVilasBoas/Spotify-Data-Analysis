using SpotifyDataAnalysis.Api.Observability;
using SpotifyDataAnalysis.SharedKernel.Observability;

namespace SpotifyDataAnalysis.Api.Middleware;

/// <summary>
/// Resolves the correlation id for every request and makes it observable end to end.
///
/// <list type="number">
///   <item>Reads <c>X-Correlation-Id</c> from the request; if absent or blank, generates a new
///   <c>Guid.NewGuid().ToString("N")</c>.</item>
///   <item>Stores it on the scoped <see cref="CorrelationIdAccessor"/> so the CQRS <c>LoggingBehavior</c>,
///   the RFC 7807 <c>traceId</c> and any handler can read it via <see cref="ICorrelationIdAccessor"/>.</item>
///   <item>Echoes it back on the response <c>X-Correlation-Id</c> header — set before the response starts,
///   so it is present even on downstream failures.</item>
/// </list>
///
/// Registered near the top of the pipeline (right after the exception boundary) so the id is available to
/// every downstream middleware, endpoint and log line.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    /// <summary>Inbound/outbound header carrying the correlation id.</summary>
    public const string HeaderName = ObservabilityConstants.CorrelationIdHeader;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICorrelationIdAccessor accessor)
    {
        string correlationId = ResolveCorrelationId(context);

        // The scoped accessor is always the concrete holder; set the resolved id so downstream consumers
        // (LoggingBehavior, ProblemDetails traceId) observe the same value for this request.
        if (accessor is CorrelationIdAccessor holder)
            holder.Set(correlationId);

        // Echo the id back before the response is flushed, so it is present even when a downstream
        // component fails and the exception handler writes the body.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var inbound))
        {
            string? candidate = inbound.ToString();
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;
        }

        return Guid.NewGuid().ToString("N");
    }
}
