using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpotifyDataAnalysis.Infrastructure.DependencyInjection;
using SpotifyDataAnalysis.Modules.Catalog.Application;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.DependencyInjection;

/// <summary>
/// Composição de DI do módulo Catalog. Chamado por <see cref="CatalogModule"/> durante o bootstrap.
///
/// E0.1: registra os handlers CQRS da Application por varredura (ainda não há nenhum).
/// E0.2 adiciona aqui o cliente da API do Spotify (<c>AddHttpClient&lt;ISpotifyClient, SpotifyApiClient&gt;</c>).
/// E1 adiciona o <c>CatalogDbContext</c> (write-side EF), repositórios, UnitOfWork/Outbox e o TransactionBehavior.
/// </summary>
public static class CatalogModuleServiceExtensions
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Handlers CQRS deste módulo (varredura da Application). O mediator os resolve por tipo de request.
        services.AddHandlersFromAssembly(typeof(CatalogApplicationAssemblyReference).Assembly);

        return services;
    }
}
