using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Infrastructure.Messaging;

/// <summary>
/// <see cref="IMediator"/> implementation with pipeline behavior support.
///
/// Pipeline order (by DI registration order):
/// ValidationBehavior → LoggingBehavior → TransactionBehavior → IRequestHandler
///
/// Reflection is used to build the pipeline with the correct concrete types known only at runtime.
/// The invoker MethodInfo is cached in a ConcurrentDictionary to avoid per-request reflection overhead.
/// Open-generic behaviors (registered as IPipelineBehavior&lt;&gt;) are resolved via
/// IEnumerable&lt;IPipelineBehavior&lt;TRequest, TResult&gt;&gt; and chained in order.
/// </summary>
public sealed class Mediator : IMediator
{
    // MethodInfo cache for the internal generic method — avoids GetMethod per request.
    private static readonly ConcurrentDictionary<Type, MethodInfo> DispatchCache = new();

    private readonly IServiceProvider _serviceProvider;

    public Mediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Get the concrete type at runtime (e.g. CreateItemCommand) to resolve the correct handlers.
        Type requestType = request.GetType();

        MethodInfo dispatchMethod = DispatchCache.GetOrAdd(
            requestType,
            static rt => typeof(Mediator)
                .GetMethod(nameof(DispatchAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(rt, typeof(TResult)));

        return (Task<TResult>)dispatchMethod.Invoke(this, [request, cancellationToken])!;
    }

    /// <summary>
    /// Internal generic method that builds and executes the full pipeline.
    /// TRequest and TResult are known at compile-time here — behaviors are resolved correctly.
    /// </summary>
    private async Task<TResult> DispatchAsync<TRequest, TResult>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        // Resolve the terminal handler.
        IRequestHandler<TRequest, TResult> handler =
            _serviceProvider.GetRequiredService<IRequestHandler<TRequest, TResult>>();

        // Resolve all behaviors registered for this (TRequest, TResult) pair.
        IEnumerable<IPipelineBehavior<TRequest, TResult>> behaviors =
            _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResult>>();

        // Build the delegate chain from outermost to innermost (last registered = most inner).
        RequestHandlerDelegate<TResult> pipeline = behaviors
            .Reverse()
            .Aggregate(
                seed: (RequestHandlerDelegate<TResult>)(() =>
                    handler.HandleAsync(request, cancellationToken)),
                func: (next, behavior) =>
                    () => behavior.HandleAsync(request, next, cancellationToken));

        return await pipeline();
    }
}
