using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpotifyDataAnalysis.Infrastructure.Data;
using SpotifyDataAnalysis.Infrastructure.Messaging;
using SpotifyDataAnalysis.Infrastructure.Messaging.Behaviors;
using SpotifyDataAnalysis.Infrastructure.Observability;
using SpotifyDataAnalysis.SharedKernel.Messaging;
using SpotifyDataAnalysis.SharedKernel.Observability;
using SpotifyDataAnalysis.SharedKernel.Time;

namespace SpotifyDataAnalysis.Infrastructure.DependencyInjection;

public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers the cross-cutting shared infrastructure every module and host builds on: the lightweight
    /// <see cref="IMediator"/>, the <see cref="IClock"/>, the Dapper read-side connection factory, the
    /// correlation-id accessor and the module-agnostic CQRS pipeline behaviors (logging + validation).
    /// The write-side <see cref="Persistence.IUnitOfWork"/>/TransactionBehavior is registered per module,
    /// because it depends on the module's own DbContext.
    /// </summary>
    public static IServiceCollection AddSpotifyInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IMediator, Mediator>();
        services.AddScoped<DbConnectionFactory>();

        // Correlation id accessor. The LoggingBehavior stamps every log line with it. TryAdd so the Host
        // can override with its request-aware accessor (populated from the X-Correlation-Id header);
        // background jobs and tests fall back to this ambient per-scope id.
        services.TryAddScoped<ICorrelationIdAccessor, AmbientCorrelationIdAccessor>();

        // Teach Dapper (the read-side) to bind DateOnly/TimeOnly parameters and columns. Dapper 2.1.x does
        // not map these types natively, so without this every read query that passes a DateOnly parameter
        // would throw NotSupportedException before reaching Npgsql.
        DapperDateOnlyTypeHandlers.Register();

        // Cross-cutting CQRS pipeline behaviors that carry NO module-specific dependency.
        // Registration order defines pipeline position: the Mediator reverses the resolved sequence, so the
        // FIRST registered behavior becomes the OUTERMOST wrapper.
        //   Logging (outermost — observes everything, including validation failures)
        //     → Validation (short-circuits before the handler on invalid input)
        //       → [TransactionBehavior — registered per write-side module]
        //         → Handler
        // TransactionBehavior is NOT registered here on purpose: it depends on IUnitOfWork, which is a
        // per-module (per-DbContext) service. Registering it globally would force every module that
        // dispatches through the mediator (even query-only ones) to register an otherwise-unused IUnitOfWork.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }
}
