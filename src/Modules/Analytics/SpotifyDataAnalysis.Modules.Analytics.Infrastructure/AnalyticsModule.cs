using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpotifyDataAnalysis.Infrastructure.Modules;
using SpotifyDataAnalysis.Modules.Analytics.Application;
using SpotifyDataAnalysis.Modules.Analytics.Infrastructure.DependencyInjection;

namespace SpotifyDataAnalysis.Modules.Analytics.Infrastructure;

/// <summary>
/// Ponto de entrada (composition root) do módulo <b>Analytics</b> para o Composition Root do Host.
///
/// Como o Catalog, vive na Infrastructure — a camada mais externa do módulo — mantendo a direção de
/// dependência para dentro (Infrastructure → Application → Domain). O Host descobre este <see cref="IModule"/>
/// por varredura de assembly e NUNCA referencia o Domain interno (garantido pelos ArchitectureTests).
///
/// <para>Acoplamento consciente (DP-1): o read-side de Analytics LÊ o schema <c>catalog</c> via Dapper. É
/// acoplamento por DADO, não por código .NET — Analytics não referencia nenhum tipo/projeto do Catalog, e
/// as regras de isolamento (que checam dependências entre assemblies) continuam verdes.</para>
/// </summary>
public sealed class AnalyticsModule : IModule
{
    /// <summary>
    /// Depois do Catalog (Order 10): Analytics só lê o schema do Catalog e não contribui com serviços dos
    /// quais o Catalog dependa, então a ordem relativa é indiferente para a correção — mantida acima do
    /// Catalog apenas para leitura previsível do bootstrap.
    /// </summary>
    public int Order => 20;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Controllers MVC deste módulo (co-locados na Application): registra o ApplicationPart para o MVC
        // do Host descobrir as actions. AddControllers é idempotente entre módulos.
        services.AddControllers().AddApplicationPart(typeof(AnalyticsApplicationAssemblyReference).Assembly);

        services.AddAnalyticsModule(configuration);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Sem Minimal API endpoints: o read-side é exposto por controllers MVC co-locados na Application.
    }
}
