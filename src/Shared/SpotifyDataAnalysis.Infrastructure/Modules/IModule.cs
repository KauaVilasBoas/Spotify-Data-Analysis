using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SpotifyDataAnalysis.Infrastructure.Modules;

/// <summary>
/// Public contract for a module in the modular monolith.
/// Each bounded context implements this in its Application project, exposing itself to the host
/// without leaking its internal Domain.
///
/// The host discovers modules by assembly scanning and invokes RegisterServices/MapEndpoints
/// in the deterministic order defined by <see cref="Order"/>.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Load order — modules with a lower value are registered first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Registers the module's services into the DI container.
    /// Called during bootstrap, before <c>WebApplication.Build()</c>.
    /// </summary>
    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Maps the module's Minimal API endpoints into the application router.
    /// Called after <c>WebApplication.Build()</c>, during HTTP pipeline setup.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
