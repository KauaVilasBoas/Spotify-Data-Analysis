using SpotifyDataAnalysis.Infrastructure.Persistence;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Infrastructure.Messaging.Behaviors;

/// <summary>
/// Pipeline behavior that wraps a <see cref="ICommand"/> or <see cref="ICommand{TResult}"/>
/// handler in a transaction via all registered <see cref="IUnitOfWork"/> instances.
///
/// For Commands: calls SaveChangesAsync on every registered IUnitOfWork after the handler
/// succeeds. Each module registers its own IUnitOfWork (e.g. EfUnitOfWork&lt;CatalogDbContext&gt;) —
/// injecting IEnumerable&lt;IUnitOfWork&gt; ensures the correct module's DbContext is committed regardless
/// of registration order. SaveChanges on a DbContext with no tracked changes is a no-op, so iterating all
/// is safe.
///
/// For Queries (IQuery): no-op — queries must not trigger write transactions.
///
/// Pipeline order: ValidationBehavior → LoggingBehavior → TransactionBehavior → Handler
/// </summary>
public sealed class TransactionBehavior<TRequest, TResult> : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    private readonly IEnumerable<IUnitOfWork> _unitOfWorks;

    public TransactionBehavior(IEnumerable<IUnitOfWork> unitOfWorks)
    {
        _unitOfWorks = unitOfWorks;
    }

    /// <inheritdoc />
    public async Task<TResult> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResult> next,
        CancellationToken cancellationToken = default)
    {
        // Queries must not trigger SaveChanges — bypass the behavior.
        if (request is not (ICommand or ICommand<TResult>))
            return await next();

        TResult result = await next();

        foreach (IUnitOfWork unitOfWork in _unitOfWorks)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }
}
