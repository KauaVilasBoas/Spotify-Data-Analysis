using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpotifyDataAnalysis.Jobs.Configuration;
using SpotifyDataAnalysis.Jobs.Jobs;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Jobs.Tests;

/// <summary>
/// Testes do job de enriquecimento agendado (E1.8). O foco é o contrato do job: despachar um
/// <see cref="EnrichCatalogReferencesCommand"/> por tick, com o <c>BatchSize</c> configurado, e sobreviver a
/// uma falha de tick (garantida pela base <c>TimedBackgroundService</c>).
/// </summary>
public sealed class CatalogEnrichmentJobTests
{
    /// <summary>Mediator fake: registra os commands despachados e, opcionalmente, falha ao primeiro despacho.</summary>
    private sealed class RecordingMediator : IMediator
    {
        private readonly bool _fail;

        public RecordingMediator(bool fail = false) => _fail = fail;

        public List<EnrichCatalogReferencesCommand> Dispatched { get; } = [];

        public Task<TResult> SendAsync<TResult>(
            IRequest<TResult> request, CancellationToken cancellationToken = default)
        {
            var command = (EnrichCatalogReferencesCommand)(object)request;
            Dispatched.Add(command);

            if (_fail)
                throw new InvalidOperationException("Falha simulada da API do Spotify.");

            object result = new EnrichCatalogReferencesResult(
                ArtistsEnriched: 1, ArtistsNotFound: 0, ArtistsFailed: 0,
                AlbumsEnriched: 1, AlbumsNotFound: 0, AlbumsFailed: 0);

            return Task.FromResult((TResult)result);
        }
    }

    private static async Task RunOneTickAsync(RecordingMediator mediator, CatalogEnrichmentOptions options)
    {
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IMediator>(_ => mediator)
            .BuildServiceProvider();

        var job = new CatalogEnrichmentJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new JobsOptions { CatalogEnrichment = options }),
            NullLogger<CatalogEnrichmentJob>.Instance);

        await job.StartAsync(CancellationToken.None);
        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dispatches_EnrichmentCommand_WithTheConfiguredBatchSize()
    {
        var mediator = new RecordingMediator();

        await RunOneTickAsync(mediator, new CatalogEnrichmentOptions
        {
            Enabled = true,
            Interval = TimeSpan.FromHours(1),
            BatchSize = 75
        });

        EnrichCatalogReferencesCommand dispatched = Assert.Single(mediator.Dispatched);
        Assert.Equal(75, dispatched.BatchSize);
    }

    [Fact]
    public async Task FallsBackToTheCommandDefaultBatchSize_WhenNotConfigured()
    {
        var mediator = new RecordingMediator();

        await RunOneTickAsync(mediator, new CatalogEnrichmentOptions
        {
            Enabled = true,
            Interval = TimeSpan.FromHours(1),
            BatchSize = 0
        });

        EnrichCatalogReferencesCommand dispatched = Assert.Single(mediator.Dispatched);
        Assert.Equal(EnrichCatalogReferencesCommand.DefaultBatchSize, dispatched.BatchSize);
    }

    [Fact]
    public async Task SurvivesAFailedTick_WithoutTearingDownTheWorker()
    {
        var mediator = new RecordingMediator(fail: true);

        // Não lança: a base registra a exceção do tick e mantém o worker vivo.
        await RunOneTickAsync(mediator, new CatalogEnrichmentOptions
        {
            Enabled = true,
            Interval = TimeSpan.FromHours(1),
            BatchSize = 50
        });

        Assert.Single(mediator.Dispatched);
    }
}
