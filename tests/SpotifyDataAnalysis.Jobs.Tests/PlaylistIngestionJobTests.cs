using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpotifyDataAnalysis.Jobs.Configuration;
using SpotifyDataAnalysis.Jobs.Jobs;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Jobs.Tests;

/// <summary>
/// Testes do job de coleta agendada (E1.6). O foco é o <b>contrato de resiliência</b> do job: despachar um
/// command por playlist-semente e não deixar uma playlist problemática cancelar as demais.
///
/// A base <c>TimedBackgroundService</c> executa um tick imediatamente ao iniciar, então
/// <c>StartAsync</c>/<c>StopAsync</c> exercitam um ciclo completo sem esperar o intervalo.
/// </summary>
public sealed class PlaylistIngestionJobTests
{
    /// <summary>Mediator fake: registra o que foi despachado e falha nos ids marcados como problemáticos.</summary>
    private sealed class RecordingMediator : IMediator
    {
        private readonly HashSet<string> _failingPlaylistIds;

        public RecordingMediator(params string[] failingPlaylistIds)
            => _failingPlaylistIds = new HashSet<string>(failingPlaylistIds, StringComparer.Ordinal);

        public List<string> DispatchedPlaylistIds { get; } = [];

        public Task<TResult> SendAsync<TResult>(
            IRequest<TResult> request, CancellationToken cancellationToken = default)
        {
            var command = (IngestPlaylistCommand)(object)request;
            DispatchedPlaylistIds.Add(command.SpotifyPlaylistId);

            if (_failingPlaylistIds.Contains(command.SpotifyPlaylistId))
                throw new InvalidOperationException("Falha simulada da API do Spotify.");

            object result = new IngestPlaylistResult(
                Ingested: 1, Updated: 0, Skipped: 0, Duplicates: 0, Total: 1);

            return Task.FromResult((TResult)result);
        }
    }

    private static async Task RunOneTickAsync(RecordingMediator mediator, params string[] seedPlaylistIds)
    {
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IMediator>(_ => mediator)
            .BuildServiceProvider();

        var options = Options.Create(new JobsOptions
        {
            PlaylistIngestion = new PlaylistIngestionOptions
            {
                Enabled = true,
                Interval = TimeSpan.FromHours(6),
                SeedPlaylistIds = seedPlaylistIds
            }
        });

        var job = new PlaylistIngestionJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<PlaylistIngestionJob>.Instance);

        await job.StartAsync(CancellationToken.None);
        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dispatches_OneIngestionCommand_PerSeedPlaylist()
    {
        var mediator = new RecordingMediator();

        await RunOneTickAsync(mediator, "pl1", "pl2", "pl3");

        Assert.Equal(new[] { "pl1", "pl2", "pl3" }, mediator.DispatchedPlaylistIds);
    }

    [Fact]
    public async Task KeepsCollecting_WhenOnePlaylistFails()
    {
        var mediator = new RecordingMediator(failingPlaylistIds: "pl2");

        await RunOneTickAsync(mediator, "pl1", "pl2", "pl3");

        // pl2 falhou, mas pl3 foi coletada mesmo assim.
        Assert.Equal(new[] { "pl1", "pl2", "pl3" }, mediator.DispatchedPlaylistIds);
    }

    [Fact]
    public async Task DoesNothing_WhenNoSeedPlaylistIsConfigured()
    {
        var mediator = new RecordingMediator();

        await RunOneTickAsync(mediator);

        Assert.Empty(mediator.DispatchedPlaylistIds);
    }
}
