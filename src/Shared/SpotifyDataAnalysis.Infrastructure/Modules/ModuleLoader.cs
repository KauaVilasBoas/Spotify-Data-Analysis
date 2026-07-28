using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SpotifyDataAnalysis.Infrastructure.Modules;

/// <summary>
/// Composition Root: discovers all <see cref="IModule"/> implementations in the provided
/// assemblies, orders them by <see cref="IModule.Order"/>, and delegates service registration
/// and endpoint mapping.
///
/// The host only needs a reference to each module's entry-point assembly — never to its
/// internal Domain or Infrastructure projects.
/// </summary>
public static class ModuleLoader
{
    private static IReadOnlyList<IModule>? _modules;

    /// <summary>
    /// Discovers modules in the provided assemblies and registers their services.
    /// Must be called before <c>WebApplication.Build()</c>.
    /// </summary>
    public static void RegisterModules(
        IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<Assembly> moduleAssemblies)
    {
        _modules = DiscoverModules(moduleAssemblies);

        foreach (IModule module in _modules)
            module.RegisterServices(services, configuration);
    }

    /// <summary>
    /// Maps the endpoints of all loaded modules.
    /// Must be called after <c>WebApplication.Build()</c>.
    /// </summary>
    public static void MapModuleEndpoints(IEndpointRouteBuilder endpoints)
    {
        if (_modules is null)
            throw new InvalidOperationException(
                $"No modules were loaded. Make sure to call {nameof(RegisterModules)} before {nameof(MapModuleEndpoints)}.");

        foreach (IModule module in _modules)
            module.MapEndpoints(endpoints);
    }

    private static IReadOnlyList<IModule> DiscoverModules(IEnumerable<Assembly> assemblies)
        => assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IModule).IsAssignableFrom(type)
                           && type is { IsAbstract: false, IsInterface: false })
            .Select(type => (IModule)Activator.CreateInstance(type)!)
            .OrderBy(module => module.Order)
            .ToList()
            .AsReadOnly();
}
