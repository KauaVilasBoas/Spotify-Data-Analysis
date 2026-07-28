using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Infrastructure.DependencyInjection;

/// <summary>
/// Registers CQRS handlers by assembly scan so a module never enumerates its handlers by hand.
/// A handler is any concrete type that closes <see cref="IRequestHandler{TRequest,TResult}"/> —
/// the single service the <c>Mediator</c> resolves. <see cref="ICommandHandler{TCommand}"/> and
/// <see cref="IQueryHandler{TQuery,TResult}"/> derive from it, so closing either one is enough to
/// be discovered. Each closed interface maps to exactly one implementation (the mediator resolves
/// by concrete request type), hence Scoped registration mirrors the previous manual wiring.
/// </summary>
public static class HandlerRegistrationExtensions
{
    private static readonly Type RequestHandlerOpenType = typeof(IRequestHandler<,>);

    public static IServiceCollection AddHandlersFromAssembly(
        this IServiceCollection services,
        Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        IEnumerable<Type> concreteTypes = assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false });

        foreach (Type implementation in concreteTypes)
        {
            IEnumerable<Type> handlerInterfaces = implementation
                .GetInterfaces()
                .Where(IsClosedRequestHandler);

            foreach (Type serviceType in handlerInterfaces)
            {
                services.AddScoped(serviceType, implementation);
            }
        }

        return services;
    }

    private static bool IsClosedRequestHandler(Type interfaceType) =>
        interfaceType.IsGenericType &&
        interfaceType.GetGenericTypeDefinition() == RequestHandlerOpenType;
}
