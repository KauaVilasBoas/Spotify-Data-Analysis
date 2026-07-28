using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpotifyDataAnalysis.Infrastructure.DependencyInjection;
using SpotifyDataAnalysis.Infrastructure.Messaging;
using SpotifyDataAnalysis.Infrastructure.Messaging.Behaviors;
using SpotifyDataAnalysis.Infrastructure.Outbox;
using SpotifyDataAnalysis.Infrastructure.Persistence;
using SpotifyDataAnalysis.Modules.Catalog.Application;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Matching;
using SpotifyDataAnalysis.Modules.Catalog.Application.Spotify;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Albums;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Artists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Playlists.Events;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks.Events;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Ingestion;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Repositories;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Spotify;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.DependencyInjection;

/// <summary>
/// Composição de DI do módulo Catalog. Chamado por <see cref="CatalogModule"/> durante o bootstrap.
///
/// E0.1: handlers CQRS por varredura. E0.2: cliente da Spotify Web API. E0.3: token Client Credentials.
/// E0.4: handler de resiliência. E1.1: write-side EF (<c>CatalogDbContext</c>), repositórios,
/// UnitOfWork/Outbox e o TransactionBehavior do módulo.
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

        // --- Persistência (E1.1): write-side EF Core + Outbox, no schema "catalog" ---
        string? connectionString = configuration.GetConnectionString("SpotifyDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Connection string 'SpotifyDb' não configurada. Defina em User Secrets ou na variável de " +
                "ambiente ConnectionStrings__SpotifyDb.");

        services.AddDbContext<CatalogDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", schema: "catalog");
                npgsql.MigrationsAssembly(typeof(CatalogDbContext).Assembly.GetName().Name);
            }));

        // Repositórios dos agregados (interfaces no Domain, implementações EF aqui).
        services.AddScoped<ITrackRepository, TrackRepository>();
        services.AddScoped<IArtistRepository, ArtistRepository>();
        services.AddScoped<IAlbumRepository, AlbumRepository>();
        services.AddScoped<IPlaylistRepository, PlaylistRepository>();

        // Registrador de referências (E1.2): upsert idempotente de artistas/álbuns descobertos na ingestão.
        // Scoped porque mantém um cache de ids já vistos válido pelo tempo do command.
        services.AddScoped<CatalogReferenceRegistrar>();

        // Leitor do CSV do Kaggle (E1.4): importa audio-features e casa por track_id.
        services.AddScoped<IKaggleAudioFeaturesReader, KaggleAudioFeaturesCsvReader>();

        // Estratégias de casamento CSV → catálogo (E1.4). A ORDEM DE REGISTRO é a ordem da chain: o
        // casamento exato por track_id vem primeiro e o fallback textual (nome+artista) só é tentado quando
        // aquele falha. Acrescentar uma estratégia (ISRC, duração aproximada) é acrescentar uma linha aqui.
        services.AddScoped<ITrackMatchingStrategy, SpotifyTrackIdMatchingStrategy>();
        services.AddScoped<ITrackMatchingStrategy, NameAndArtistMatchingStrategy>();
        services.AddScoped<TrackMatcher>();

        // Write-side UnitOfWork + Outbox (estratégia híbrida de consistência):
        //  - IOutboxDbContext aponta para o DbContext do módulo (write do agregado + outbox na MESMA transação);
        //  - OutboxWriter serializa integration events; IDomainEventDispatcher despacha e enfileira no Outbox;
        //  - IUnitOfWork = EfUnitOfWork<CatalogDbContext>: o SaveChanges é disparado pelo TransactionBehavior.
        services.AddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<CatalogDbContext>());
        services.AddScoped<OutboxWriter>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork<CatalogDbContext>>();

        // TransactionBehavior registrado AQUI (depende do IUnitOfWork deste módulo). Após os behaviors
        // compartilhados Logging/Validation, o mediator resolve: Logging → Validation → Transaction → Handler.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        // DomainEvent → IntegrationEvent (E1.3): o DomainEventDispatcher resolve o translator por tipo de
        // evento durante o SaveChanges e enfileira o TrackIngested no Outbox.
        services.AddScoped<IDomainEventToIntegrationEventTranslator<TrackRegisteredDomainEvent>,
            TrackRegisteredToIntegrationEventTranslator>();

        services.AddScoped<IDomainEventToIntegrationEventTranslator<PlaylistIngestedDomainEvent>,
            PlaylistIngestedToIntegrationEventTranslator>();

        return services;
    }
}
