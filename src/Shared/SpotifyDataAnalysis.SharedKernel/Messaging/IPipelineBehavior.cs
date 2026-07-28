namespace SpotifyDataAnalysis.SharedKernel.Messaging;

public interface IPipelineBehavior<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResult> next,
        CancellationToken cancellationToken = default);
}

public delegate Task<TResult> RequestHandlerDelegate<TResult>();
