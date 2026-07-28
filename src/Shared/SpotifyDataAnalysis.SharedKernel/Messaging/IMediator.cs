namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>
/// Lightweight CQRS dispatcher — resolves the matching handler from DI and dispatches the request.
/// Does not depend on MediatR or any external library; implemented in SpotifyDataAnalysis.Infrastructure.
/// </summary>
public interface IMediator
{
    Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);
}
