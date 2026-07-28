using FluentValidation;
using FluentValidation.Results;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Infrastructure.Messaging.Behaviors;

/// <summary>
/// Pipeline behavior that runs FluentValidation validators before the handler.
/// If no <see cref="IValidator{TRequest}"/> is registered, the request passes through.
/// If there are failures, throws <see cref="ValidationException"/> — the handler is never called.
///
/// Validators are discovered via FluentValidation DI extensions per module.
/// Pipeline order: ValidationBehavior → LoggingBehavior → TransactionBehavior → Handler
/// (registered first = executes outermost in the chain)
/// </summary>
public sealed class ValidationBehavior<TRequest, TResult> : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    /// <inheritdoc />
    public async Task<TResult> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResult> next,
        CancellationToken cancellationToken = default)
    {
        if (!_validators.Any())
            return await next();

        ValidationContext<TRequest> context = new(request);

        ValidationResult[] results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        List<ValidationFailure> failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
