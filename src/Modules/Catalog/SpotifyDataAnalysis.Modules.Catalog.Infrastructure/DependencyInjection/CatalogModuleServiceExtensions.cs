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
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Imputation;
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
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;
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

        // Seed do catálogo a partir do CSV do Kaggle (E1.10): cria faixas direto do dataset, em lotes, pelo
        // DbContext cru (sem Outbox). Disparado pela CLI dev do Host (`seed-catalog`), não por endpoint.
        services.AddScoped<KaggleCatalogSeeder>();

        // Seed de referências (E1.11): deriva artistas/álbuns dos NOMES do CSV e liga as faixas já semeadas.
        // Também pela CLI dev do Host (`seed-references`), rodado depois do `seed-catalog`.
        services.AddScoped<KaggleReferenceSeeder>();

        // Estratégias de casamento CSV → catálogo (E1.4/E1.9). A ORDEM DE REGISTRO É a ordem da chain no
        // TrackMatcher — da mais confiável para a menos confiável — e é aqui, na composição, que essa
        // precedência fica registrada (não escondida no matcher):
        //  1. SpotifyTrackId     — casamento exato por track_id; quando bate, não há dúvida.
        //  2. NameAndDuration    — chave textual "artista+título" CONFIRMADA pela duração da gravação;
        //                          desambigua homônimos do mesmo artista que a chave textual pura confunde.
        //  3. NameAndArtist      — fallback textual puro; último recurso, escolhe a 1ª ocorrência da chave.
        // A #2 vem ANTES da #3 de propósito: só assume quando a duração confirma uma candidata, e cede a vez
        // (retorna null) caso contrário — então nunca "rouba" um caso que a #3 resolveria. Acrescentar uma
        // estratégia é acrescentar uma linha aqui, na posição certa da precedência.
        services.AddScoped<ITrackMatchingStrategy, SpotifyTrackIdMatchingStrategy>();
        services.AddScoped<ITrackMatchingStrategy, NameAndDurationMatchingStrategy>();
        services.AddScoped<ITrackMatchingStrategy, NameAndArtistMatchingStrategy>();
        services.AddScoped<TrackMatcher>();

        // Política de tratamento de faltantes (E1.5): mediana por gênero com retaguarda global. Trocar a
        // política (kNN, exclusão do treino) é trocar esta implementação — o handler não muda.
        services.AddScoped<IAudioFeatureImputer, MedianAudioFeatureImputer>();

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
