using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Recommendations;

/// <summary>
/// Monta o índice de similaridade em background durante o arranque do servidor, sem bloquear o início do HTTP
/// pipeline. Enquanto a montagem não termina, requisições a <c>GET /api/recommendations/track/{id}</c> recebem
/// uma resposta 503 com <c>Retry-After</c> — honesta e imediata, em vez de ficarem penduradas.
///
/// <para>É um <see cref="BackgroundService"/> de uso único (não periódico): chama <see cref="CachedTrackSimilarityIndexProvider.WarmUpAsync"/>
/// uma vez no arranque e encerra. O provider é idempotente — chamadas duplicadas não refazem a varredura. Se falhar,
/// a próxima requisição a <c>GET /api/recommendations/track/{id}</c> montará o índice sob demanda.</para>
/// </summary>
internal sealed partial class SimilarityIndexWarmUpService : BackgroundService
{
    private readonly CachedTrackSimilarityIndexProvider _provider;
    private readonly ILogger<SimilarityIndexWarmUpService> _logger;

    public SimilarityIndexWarmUpService(
        CachedTrackSimilarityIndexProvider provider,
        ILogger<SimilarityIndexWarmUpService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarting(_logger);

        try
        {
            await _provider.WarmUpAsync(stoppingToken);
            LogReady(_logger);
        }
        catch (OperationCanceledException)
        {
            // Cancelamento normal no shutdown — não é erro.
        }
        catch (Exception ex)
        {
            LogFailed(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Iniciando montagem do índice de similaridade em background.")]
    private static partial void LogStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Índice de similaridade disponível para requisições.")]
    private static partial void LogReady(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao montar o índice de similaridade no warm-up.")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
