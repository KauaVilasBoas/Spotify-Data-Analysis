using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpotifyDataAnalysis.Infrastructure.DependencyInjection;
using SpotifyDataAnalysis.Modules.Analytics.Application;

namespace SpotifyDataAnalysis.Modules.Analytics.Infrastructure.DependencyInjection;

/// <summary>
/// Composição de DI do módulo Analytics (read-side de EDA). Chamado por <see cref="AnalyticsModule"/>
/// durante o bootstrap.
///
/// O módulo é SÓ leitura: registra os QueryHandlers da Application por varredura. Não há DbContext,
/// UnitOfWork nem TransactionBehavior (o read-side não muda estado). O <c>DbConnectionFactory</c> usado pelos
/// handlers Dapper (via <c>BaseDataAccess</c>) já é registrado pela Infrastructure compartilhada
/// (<c>AddSpotifyInfrastructure</c>), então não é re-registrado aqui.
/// </summary>
public static class AnalyticsModuleServiceExtensions
{
    public static IServiceCollection AddAnalyticsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Handlers CQRS (queries) deste módulo, descobertos por varredura da Application. O mediator os
        // resolve por tipo de request.
        services.AddHandlersFromAssembly(typeof(AnalyticsApplicationAssemblyReference).Assembly);

        return services;
    }
}
