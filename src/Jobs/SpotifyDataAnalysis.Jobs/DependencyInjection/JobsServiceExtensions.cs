using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpotifyDataAnalysis.Infrastructure.Messaging;
using SpotifyDataAnalysis.Infrastructure.Outbox;
using SpotifyDataAnalysis.Jobs.Configuration;
using SpotifyDataAnalysis.Jobs.Jobs;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Jobs.DependencyInjection;

/// <summary>
/// Composition of the SpotifyDataAnalysis background jobs.
///
/// <c>SpotifyDataAnalysis.Jobs</c> is a library, not a process: the host (currently
/// <c>SpotifyDataAnalysis.Api</c>) calls <see cref="AddSpotifyJobs"/> to register the scheduled
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/>s and their supporting services into its own
/// container. The jobs then share the API's DI graph — the same module DbContexts, the same Outbox, the
/// same <c>IClock</c> — with no second composition root.
///
/// This method must be called AFTER the modules are registered (they contribute <c>IOutboxDbContext</c>
/// and the write-side DbContexts the Outbox dispatcher depends on).
/// </summary>
public static class JobsServiceExtensions
{
    public static IServiceCollection AddSpotifyJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. Bind per-job configuration (intervals, batch sizes) from the "Jobs" section. Absent keys fall
        //    back to JobsOptions' defaults, so the worker runs with zero configuration.
        services
            .AddOptions<JobsOptions>()
            .Bind(configuration.GetSection(JobsOptions.SectionName));

        // 2. Integration event bus. The OutboxDispatcher publishes through it, and the in-process
        //    InMemoryEventBus fans out to the module IIntegrationEventHandler<T>s. TryAdd keeps it
        //    idempotent if a future host (or a broker-backed bus) registers one first.
        services.TryAddSingleton<IEventBus, InMemoryEventBus>();

        // 3. The Outbox dispatcher. Scoped because it depends on the scoped IOutboxDbContext (a module
        //    DbContext); the job resolves it from its per-tick scope. Registered here (not in a module)
        //    because it is host/background infrastructure, not a module concern.
        services.TryAddScoped<OutboxDispatcher>();

        // 4. The scheduled jobs. Registered as IHostedService so the host's generic-host lifetime
        //    starts/stops them.
        services.AddHostedService<OutboxDispatcherJob>();

        // 5. Scheduled playlist collection (E1.6). Registered ONLY when explicitly enabled: it calls the
        //    Spotify Web API, so it must not start just because the host booted — an environment without
        //    credentials or seeds would burn ticks failing. The flag is read here (not inside the job)
        //    so a disabled job is never even instantiated.
        bool playlistIngestionEnabled = configuration
            .GetSection($"{JobsOptions.SectionName}:{nameof(JobsOptions.PlaylistIngestion)}:{nameof(PlaylistIngestionOptions.Enabled)}")
            .Get<bool>();

        if (playlistIngestionEnabled)
            services.AddHostedService<PlaylistIngestionJob>();

        // 6. Scheduled catalog enrichment (E1.8). Registered ONLY when explicitly enabled, for the same
        //    reason as the playlist collection: it calls the Spotify Web API (heavily), so it must not start
        //    just because the host booted. Read here so a disabled job is never even instantiated.
        bool catalogEnrichmentEnabled = configuration
            .GetSection($"{JobsOptions.SectionName}:{nameof(JobsOptions.CatalogEnrichment)}:{nameof(CatalogEnrichmentOptions.Enabled)}")
            .Get<bool>();

        if (catalogEnrichmentEnabled)
            services.AddHostedService<CatalogEnrichmentJob>();

        return services;
    }
}
