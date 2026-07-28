using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.SharedKernel.Messaging;
using SpotifyDataAnalysis.SharedKernel.Observability;

namespace SpotifyDataAnalysis.Infrastructure.Messaging.Behaviors;

/// <summary>
/// Pipeline behavior that emits structured logs for request start, completion, and duration.
/// Exceptions are re-thrown after logging — this behavior never swallows errors.
///
/// Every log line carries the request's <c>CorrelationId</c>, so a single request can be stitched
/// together end to end in the log aggregator. The id is resolved from the scoped
/// <see cref="ICorrelationIdAccessor"/> the Host populates from the <c>X-Correlation-Id</c> header.
///
/// Pipeline order: ValidationBehavior → LoggingBehavior → TransactionBehavior → Handler
/// </summary>
public sealed class LoggingBehavior<TRequest, TResult> : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResult>> _logger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    public LoggingBehavior(
        ILogger<LoggingBehavior<TRequest, TResult>> logger,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _logger = logger;
        _correlationIdAccessor = correlationIdAccessor;
    }

    /// <inheritdoc />
    public async Task<TResult> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResult> next,
        CancellationToken cancellationToken = default)
    {
        string requestName = typeof(TRequest).Name;
        string correlationId = _correlationIdAccessor.CorrelationId;

        _logger.LogDebug(
            "Mediator: starting {RequestName} [{CorrelationId}]", requestName, correlationId);

        long startedAt = Environment.TickCount64;

        try
        {
            TResult result = await next();

            long elapsed = Environment.TickCount64 - startedAt;
            _logger.LogDebug(
                "Mediator: {RequestName} completed in {ElapsedMs}ms [{CorrelationId}]",
                requestName, elapsed, correlationId);

            return result;
        }
        catch (Exception ex)
        {
            long elapsed = Environment.TickCount64 - startedAt;
            _logger.LogWarning(
                "Mediator: {RequestName} failed after {ElapsedMs}ms [{CorrelationId}] | {ExceptionType}: {Message}",
                requestName, elapsed, correlationId, ex.GetType().Name, ex.Message);

            throw;
        }
    }
}
