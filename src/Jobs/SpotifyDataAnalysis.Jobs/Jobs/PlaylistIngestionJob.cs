using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyDataAnalysis.Jobs.Configuration;
using SpotifyDataAnalysis.Jobs.Scheduling;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Jobs.Jobs;

/// <summary>
/// Coleta agendada das playlists-semente (E1.6): a cada tick, despacha um
/// <see cref="IngestPlaylistCommand"/> por playlist configurada.
///
/// <b>A idempotência não é implementada aqui</b> — ela é propriedade do command: faixas já catalogadas são
/// atualizadas em vez de duplicadas e a playlist passa a refletir o último retrato visto. Por isso o job
/// pode rodar quantas vezes quiser, e não precisa de cursor, marca d'água nem estado próprio.
///
/// Cada playlist é despachada de forma <b>independente</b>: uma falha (rate limit, playlist removida) é
/// registrada e não impede a coleta das demais — nem derruba o worker, que a base já protege. Como cada
/// command tem sua própria transação, o que já foi coletado permanece.
///
/// O job conhece o módulo Catalog apenas pelo seu ponto de entrada de aplicação (o command), nunca pelo
/// Domain — a mesma fronteira que o Host respeita.
/// </summary>
public sealed class PlaylistIngestionJob : TimedBackgroundService
{
    private readonly PlaylistIngestionOptions _options;

    public PlaylistIngestionJob(
        IServiceScopeFactory scopeFactory,
        IOptions<JobsOptions> options,
        ILogger<PlaylistIngestionJob> logger)
        : base(scopeFactory, logger)
    {
        _options = options.Value.PlaylistIngestion;
    }

    /// <inheritdoc />
    protected override TimeSpan Interval => _options.Interval;

    /// <inheritdoc />
    protected override async Task ExecuteTickAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        if (_options.SeedPlaylistIds.Count == 0)
        {
            Logger.LogInformation(
                "Coleta agendada sem playlists-semente configuradas (Jobs:PlaylistIngestion:SeedPlaylistIds); nada a fazer.");
            return;
        }

        IMediator mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        foreach (string playlistId in _options.SeedPlaylistIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CollectSafelyAsync(mediator, playlistId, cancellationToken);
        }
    }

    private async Task CollectSafelyAsync(
        IMediator mediator, string playlistId, CancellationToken cancellationToken)
    {
        try
        {
            IngestPlaylistResult result = await mediator.SendAsync(
                new IngestPlaylistCommand(playlistId), cancellationToken);

            Logger.LogInformation(
                "Coleta da playlist {PlaylistId} concluída: {Ingested} novas, {Updated} atualizadas, " +
                "{Skipped} puladas, {Duplicates} repetidas, {Total} lidas.",
                playlistId, result.Ingested, result.Updated, result.Skipped, result.Duplicates, result.Total);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown no meio da coleta — propaga para a base encerrar o laço.
            throw;
        }
        catch (Exception exception)
        {
            // Uma playlist problemática não pode cancelar a coleta das outras.
            Logger.LogError(exception,
                "Falha ao coletar a playlist {PlaylistId}; as demais seguem e esta será retentada no próximo ciclo.",
                playlistId);
        }
    }
}
