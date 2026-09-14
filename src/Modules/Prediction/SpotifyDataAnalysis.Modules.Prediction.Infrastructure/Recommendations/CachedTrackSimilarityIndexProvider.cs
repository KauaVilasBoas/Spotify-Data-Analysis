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
/// <para><b>Máquina de estados explícita (<see cref="IndexBuildState"/>):</b></para>
/// <list type="number">
///   <item><b><see cref="IndexBuildState.Ready"/></b> — retorna o índice, lock-free.</item>
///   <item><b><see cref="IndexBuildState.Building"/></b> — lança <see cref="ServiceUnavailableException"/>
///     (503) imediatamente com <c>Retry-After</c>; o cliente pode fazer retry em vez de ficar pendurado.</item>
///   <item><b><see cref="IndexBuildState.NotStarted"/> com warm-up registrado</b> — lança
///     <see cref="ServiceUnavailableException"/> (503): o warm-up foi registrado e ainda não disparou;
///     uma requisição não paga a varredura nesse intervalo.</item>
///   <item><b><see cref="IndexBuildState.NotStarted"/> sem warm-up registrado</b> — monta sob demanda
///     (cenário de teste sem banco, ou uso standalone).</item>
///   <item><b><see cref="IndexBuildState.Failed"/></b> — monta sob demanda na próxima chamada; uma falha
///     do Postgres no arranque não deixa o endpoint morto para sempre.</item>
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

    private const string BuildingMessage =
        "O índice de similaridade ainda está sendo montado no arranque — aguarde alguns instantes e tente novamente.";

    private const string WarmUpPendingMessage =
        "O servidor ainda está inicializando o índice de similaridade — aguarde alguns instantes e tente novamente.";

    /// <summary>Segundos sugeridos para o cliente esperar antes de fazer retry enquanto o índice está montando.</summary>
    private const int RetryAfterSeconds = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CachedTrackSimilarityIndexProvider> _logger;

    /// <summary>
    /// <c>InitialCount = 1</c>: estado inicial livre — nenhuma montagem em andamento.
    /// Quando adquirido (Count → 0): montagem em andamento.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Estado explícito da montagem do índice. Publicado antes de qualquer await, garantindo visibilidade
    /// entre threads sem depender do contador interno do semáforo.
    /// </summary>
    private volatile IndexBuildState _state = IndexBuildState.NotStarted;

    /// <summary>
    /// Definido por <see cref="NotifyWarmUpRegistered"/>, chamado pelo construtor de
    /// <see cref="SimilarityIndexWarmUpService"/> na inicialização do hosted service — antes de qualquer
    /// requisição ser processada. Quando verdadeiro, <see cref="GetIndexAsync"/> nunca constrói sob demanda:
    /// devolve 503 até o warm-up concluir (ou falhar, momento em que volta a construir sob demanda).
    /// </summary>
    private volatile bool _warmUpRegistered;

    private volatile SimilarityIndex? _index;

    public CachedTrackSimilarityIndexProvider(
        IServiceScopeFactory scopeFactory,
        ILogger<CachedTrackSimilarityIndexProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Chamado pelo construtor de <see cref="SimilarityIndexWarmUpService"/> para sinalizar que o warm-up
    /// está registrado. A partir deste ponto, <see cref="GetIndexAsync"/> não constrói sob demanda enquanto
    /// o estado for <see cref="IndexBuildState.NotStarted"/> — evita que uma requisição no arranque pague
    /// a varredura síncrona antes de o <c>BackgroundService</c> ter a chance de disparar.
    /// </summary>
    internal void NotifyWarmUpRegistered() => _warmUpRegistered = true;

    /// <summary>
    /// Devolve o índice montado (caminho quente, lock-free se <see cref="IndexBuildState.Ready"/>).
    /// Lança <see cref="ServiceUnavailableException"/> (503 + Retry-After) imediatamente quando a montagem
    /// está em andamento ou quando o warm-up foi registrado mas ainda não disparou. Constrói sob demanda
    /// quando nunca iniciado (sem warm-up registrado) ou após falha — nenhum caminho fica pior que antes do E6.9.
    /// </summary>
    public async Task<SimilarityIndex> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        // Re-leitura explícita antes de cada decisão de estado — evita que o compilador ou o JIT
        // reutilize um valor lido anteriormente em outro branch.
        IndexBuildState state = _state;

        // Fast path: pronto — sem trava.
        if (state == IndexBuildState.Ready)
            return _index!;

        // Montagem em andamento: throw honesto e imediato.
        if (state == IndexBuildState.Building)
            throw new ServiceUnavailableException(BuildingMessage, RetryAfterSeconds);

        // NotStarted com warm-up registrado: o hosted service vai disparar em instantes.
        // Não deve construir sob demanda — isso transferiria a varredura para o request.
        if (state == IndexBuildState.NotStarted && _warmUpRegistered)
            throw new ServiceUnavailableException(WarmUpPendingMessage, RetryAfterSeconds);

        // NotStarted sem warm-up registrado, ou Failed: constrói sob demanda.
        // O semáforo impede dupla montagem mesmo com chamadas concorrentes.
        return await ExecuteBuildAsync(cancellationToken);
    }

    /// <summary>
    /// Monta o índice em background, chamado pelo <see cref="SimilarityIndexWarmUpService"/> no arranque. Bloqueia
    /// até a montagem terminar e publica o resultado em <c>_index</c>. Idempotente: se o índice já foi publicado
    /// (chamada duplicada), retorna sem refazer a varredura.
    /// </summary>
    internal async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        // Fast path: já montado (improvável no arranque normal, mas garante idempotência).
        if (_index is not null)
            return;

        await ExecuteBuildAsync(cancellationToken);
    }

    /// <summary>
    /// Protocolo unificado de montagem: publica <see cref="IndexBuildState.Building"/> antes de qualquer await,
    /// executa a varredura com double-checked locking via semáforo, e publica <see cref="IndexBuildState.Ready"/>
    /// ou <see cref="IndexBuildState.Failed"/> ao terminar. Centraliza o protocolo para que qualquer refinamento
    /// futuro seja aplicado uma única vez.
    /// </summary>
    private async Task<SimilarityIndex> ExecuteBuildAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Double-check: outro chamador pode ter montado enquanto esperávamos o semáforo.
            if (_index is not null)
                return _index;

            // Publica o estado Building antes de iniciar a varredura — visível a outras threads
            // assim que o estado for lido em GetIndexAsync.
            _state = IndexBuildState.Building;

            SimilarityIndex built = await BuildIndexAsync(cancellationToken);

            // Publica o índice antes de mudar o estado: leitores do fast path de GetIndexAsync
            // que observarem Ready verão _index não-nulo.
            _index = built;
            _state = IndexBuildState.Ready;

            return built;
        }
        catch
        {
            // Só transita para Failed se ainda não estava Ready (falha após double-check mas antes do build).
            if (_state != IndexBuildState.Ready)
                _state = IndexBuildState.Failed;
            throw;
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
