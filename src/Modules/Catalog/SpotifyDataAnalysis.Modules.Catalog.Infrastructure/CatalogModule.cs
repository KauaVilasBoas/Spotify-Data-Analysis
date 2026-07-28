using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpotifyDataAnalysis.Infrastructure.Modules;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.DependencyInjection;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure;

/// <summary>
/// Ponto de entrada (composition root) do módulo <b>Catalog</b> para o Composition Root do Host.
///
/// Vive na Infrastructure — a camada mais externa do módulo — para poder compor tudo (Application +
/// adapters) mantendo a direção de dependência estritamente para dentro (Infrastructure → Application →
/// Domain), sem o acoplamento reverso Application → Infrastructure. O Host descobre este <see cref="IModule"/>
/// por varredura de assembly e NUNCA referencia o Domain interno do módulo (garantido pelos ArchitectureTests).
/// </summary>
public sealed class CatalogModule : IModule
{
    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
        => services.AddCatalogModule(configuration);

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Sem endpoints ainda — os controllers de leitura (insights/catálogo) chegam nos épicos E1/E2.
    }
}
