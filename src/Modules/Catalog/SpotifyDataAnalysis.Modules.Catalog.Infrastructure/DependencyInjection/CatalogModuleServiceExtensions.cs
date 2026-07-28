using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpotifyDataAnalysis.Infrastructure.DependencyInjection;
using SpotifyDataAnalysis.Modules.Catalog.Application;
using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Spotify;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.DependencyInjection;

/// <summary>
/// Composição de DI do módulo Catalog. Chamado por <see cref="CatalogModule"/> durante o bootstrap.
///
/// E0.1: registra os handlers CQRS da Application por varredura.
/// E0.2: registra o cliente da Spotify Web API (opções + HttpClient tipado + seam de token).
/// E0.3: registra o provider de token real (Client Credentials, cache/refresh) como singleton.
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

        // Spotify Web API (E0.2): opções da seção "Spotify".
        services.AddOptions<SpotifyApiOptions>()
            .Bind(configuration.GetSection(SpotifyApiOptions.SectionName));

        // Provider de token (E0.3): fluxo Client Credentials com cache/refresh. Singleton para cachear o
        // token no processo inteiro (não por request/escopo); credenciais lidas via User Secrets/ambiente.
        services.AddSingleton<ISpotifyTokenProvider, SpotifyClientCredentialsTokenProvider>();

        // Handler de resiliência (E0.4): retry com backoff exponencial + 429/Retry-After, encadeado no
        // pipeline do HttpClient da Web API.
        services.AddTransient<SpotifyResilienceHandler>();

        // Cliente HTTP tipado. A BaseAddress vem da configuração; o handler de resiliência é encadeado.
        // (AddHttpClient também registra o IHttpClientFactory usado pelo provider de token.)
        services.AddHttpClient<ISpotifyClient, SpotifyApiClient>((sp, http) =>
        {
            SpotifyApiOptions options = sp.GetRequiredService<IOptions<SpotifyApiOptions>>().Value;
            http.BaseAddress = new Uri(options.BaseUrl);
        })
        .AddHttpMessageHandler<SpotifyResilienceHandler>();

        return services;
    }
}
