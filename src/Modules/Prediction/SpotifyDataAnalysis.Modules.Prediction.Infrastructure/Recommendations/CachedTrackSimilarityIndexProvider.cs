using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Recommendations;

/// <summary>
/// Cache SINGLETON do <see cref="SimilarityIndex"/> do catálogo (DP-D: on-the-fly com índice em memória). O índice
/// é montado em background no arranque pelo <see cref="SimilarityIndexWarmUpService"/> — sem bloquear o início do
/// servidor. Após montado, o caminho quente é um retorno do campo já publicado, sem trava. Mesmo princípio do
/// <c>CurrentModelCache</c> do E3.5: o custo é de CARGA, não de consulta; refazer a varredura de ~90k faixas
/// por request seria absurdo, guardar ~7 MB não é.
///
/// <para><b>Singleton lendo de um source scoped:</b> o <see cref="ISimilarityFeatureSource"/> depende do
/// <c>DbConnectionFactory</c> (scoped, per-request), então um singleton não pode injetá-lo direto. A montagem
/// abre um scope próprio via <see cref="IServiceScopeFactory"/> — o padrão canônico para um serviço de vida longa
/// consumir um colaborador de vida curta sem capturar uma conexão além do necessário.</para>
///
/// <para><b>Máquina de estados:</b> três situações distintas para <see cref="GetIndexAsync"/>:</para>
/// <list type="number">
///   <item><b>Pronto</b> (<c>_index != null</c>) — retorna o índice, lock-free.</item>
///   <item><b>Montagem em andamento</b> (<c>_gate.CurrentCount == 0</c>) — lança
///     <see cref="ServiceUnavailableException"/> (503) imediatamente com <c>Retry-After</c>; o cliente
///     pode fazer retry em vez de ficar pendurado.</item>
///   <item><b>Nunca iniciada ou terminada em falha</b> — monta sob demanda, preservando o comportamento
///     anterior ao E6.9: uma falha do Postgres no arranque não deixa o endpoint morto para sempre.</item>
/// </list>
///
/// <para>O <see cref="SemaphoreSlim"/> serializa a montagem: duas chamadas simultâneas ao <see cref="WarmUpAsync"/>
/// ou ao caminho sob-demanda de <see cref="GetIndexAsync"/> não disparam duas varreduras do catálogo.</para>
/// </summary>
internal sealed partial class CachedTrackSimilarityIndexProvider : ITrackSimilarityIndexProvider, IDisposable
{
    /// <summary>
    /// Lote de leitura da varredura de montagem. Grande porque é uma varredura única do catálogo inteiro e um
    /// lote maior reduz idas ao banco; ainda dentro do teto do source, que limita o pico de memória do transporte.
    /// </summary>
    internal const int IndexBuildBatchSize = 20_000;

    private const string NotReadyMessage =
        "O índice de similaridade ainda está sendo montado no arranque — aguarde alguns instantes e tente novamente.";

    /// <summary>Segundos sugeridos para o cliente esperar antes de fazer retry enquanto o índice está montando.</summary>
    private const int RetryAfterSeconds = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CachedTrackSimilarityIndexProvider> _logger;

    /// <summary>
    /// <c>InitialCount = 1</c>: estado inicial livre — nenhuma montagem em andamento.
    /// Quando adquirido (Count → 0): montagem em andamento.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile SimilarityIndex? _index;

    public CachedTrackSimilarityIndexProvider(
        IServiceScopeFactory scopeFactory,
        ILogger<CachedTrackSimilarityIndexProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Devolve o índice montado (caminho quente, lock-free). Se a montagem já está em andamento,
    /// lança <see cref="ServiceUnavailableException"/> (503 + Retry-After) imediatamente. Se nunca
    /// foi iniciada ou terminou em falha, monta sob demanda — nenhum caminho fica pior que antes do E6.9.
    /// </summary>
    public async Task<SimilarityIndex> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: pronto — sem trava.
        SimilarityIndex? index = _index;
        if (index is not null)
            return index;

        // Montagem em andamento: gate ocupado por outro chamador.
        // Resposta honesta e imediata (503) em vez de fila de espera.
        if (_gate.CurrentCount == 0)
            throw new ServiceUnavailableException(NotReadyMessage, RetryAfterSeconds);

        // Nunca iniciada ou terminou em falha: monta sob demanda.
        // O SemaphoreSlim evita dupla montagem mesmo com chamadas concorrentes.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Double-check: outro chamador pode ter montado enquanto esperávamos.
            if (_index is not null)
                return _index;

            // Se outro chamador já ocupa o gate (estado "em andamento") e chegamos aqui,
            // o WaitAsync nos bloqueou até ele terminar — verificamos o double-check acima.
            // Chegamos aqui apenas se realmente precisamos montar.
            _index = await BuildIndexAsync(cancellationToken);
            return _index;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Monta o índice em background, chamado pelo <see cref="SimilarityIndexWarmUpService"/> no arranque. Bloqueia
    /// até a montagem terminar e publica o resultado em <c>_index</c>. Idempotente: se o índice já foi publicado
    /// (chamada duplicada), retorna sem refazer a varredura. O <see cref="SemaphoreSlim"/> impede que duas chamadas
    /// simultâneas disparem duas varreduras do catálogo.
    /// </summary>
    internal async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        // Fast path: já montado (improvável no arranque normal, mas garante idempotência).
        if (_index is not null)
            return;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_index is not null)
                return;

            _index = await BuildIndexAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Varre o source, materializa as faixas cruas elegíveis e delega a <see cref="SimilarityIndex.Build"/> — que
    /// aprende μ/σ e normaliza tudo. Mede tempo e pico de memória da montagem (o card pede registrar o custo do
    /// índice ao lado da latência de consulta) e os registra em log.
    /// </summary>
    private async Task<SimilarityIndex> BuildIndexAsync(CancellationToken cancellationToken)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        long managedMemoryBefore = GC.GetTotalMemory(forceFullCollection: false);

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var featureSource = scope.ServiceProvider.GetRequiredService<ISimilarityFeatureSource>();

        var rawTracks = new List<RawTrackFeatures>(capacity: 100_000);

        await foreach (RawTrackFeatures track in
            featureSource.StreamEligibleTracksAsync(IndexBuildBatchSize, cancellationToken))
        {
            rawTracks.Add(track);
        }

        SimilarityIndex index = SimilarityIndex.Build(rawTracks);

        TimeSpan buildDuration = Stopwatch.GetElapsedTime(startTimestamp);
        long managedMemoryAfter = GC.GetTotalMemory(forceFullCollection: false);
        double indexFootprintMb = Math.Max(0, managedMemoryAfter - managedMemoryBefore) / (1024.0 * 1024.0);

        LogIndexBuilt(_logger, index.Count, buildDuration.TotalMilliseconds, indexFootprintMb);

        return index;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Índice de similaridade montado: {TrackCount} faixas em {BuildMs:F0} ms (pico aprox. de memória do índice: {FootprintMb:F1} MB).")]
    private static partial void LogIndexBuilt(ILogger logger, int trackCount, double buildMs, double footprintMb);
}
