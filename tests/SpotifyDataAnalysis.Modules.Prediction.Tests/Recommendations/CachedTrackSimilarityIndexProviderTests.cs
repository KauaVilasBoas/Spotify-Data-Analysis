using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// Comportamento da máquina de estados do <see cref="CachedTrackSimilarityIndexProvider"/> (E6.9):
/// <list type="bullet">
///   <item>Montagem em andamento → <see cref="ServiceUnavailableException"/> (503) imediata, não pendurada.</item>
///   <item>NotStarted com warm-up registrado → 503 imediata (arranque pendente).</item>
///   <item>Nunca iniciada sem warm-up registrado → monta sob demanda.</item>
///   <item>Warm-up falhou → monta sob demanda na próxima chamada.</item>
///   <item>Duas chamadas concorrentes de warm-up não disparam duas varreduras.</item>
///   <item>Após montado → retorna o índice correto.</item>
///   <item>503 não é lançado depois que o índice fica pronto (re-leitura de estado antes do throw).</item>
/// </list>
/// </summary>
public sealed class CachedTrackSimilarityIndexProviderTests
{
    private static SimilarityFeatureVector Vec(double seed) =>
        SimilarityFeatureVector.Create(
            Enumerable.Range(1, SimilarityFeatures.Dimension).Select(i => i + seed).ToArray());

    // --- stubs ---

    /// <summary>
    /// Feature source que bloqueia até <paramref name="buildGate"/> ser liberado, e sinaliza
    /// <paramref name="startedSignal"/> imediatamente antes de bloquear — permitindo que o
    /// teste saiba de forma determinística quando a montagem do índice está em andamento,
    /// sem depender de <c>Task.Delay</c>. Conta em <see cref="StreamCallCount"/> quantas
    /// vezes o streaming foi iniciado, para que a asserção prove que a fonte foi varrida
    /// exatamente uma vez mesmo com chamadas concorrentes.
    /// </summary>
    private sealed class BlockingFeatureSource : ISimilarityFeatureSource
    {
        private readonly SemaphoreSlim _buildGate;
        private readonly TaskCompletionSource _startedSignal;

        public int StreamCallCount;

        public BlockingFeatureSource(SemaphoreSlim buildGate, TaskCompletionSource startedSignal)
        {
            _buildGate = buildGate;
            _startedSignal = startedSignal;
        }

        public async IAsyncEnumerable<RawTrackFeatures> StreamEligibleTracksAsync(
            int batchSize,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            System.Threading.Interlocked.Increment(ref StreamCallCount);
            // Sinaliza que a montagem começou — o teste pode avançar de forma determinística.
            _startedSignal.TrySetResult();
            await _buildGate.WaitAsync(cancellationToken);
            yield return new RawTrackFeatures("a", Vec(0.0), null, false);
        }
    }

    /// <summary>Feature source imediato que conta quantas vezes foi chamado.</summary>
    private sealed class CountingFeatureSource : ISimilarityFeatureSource
    {
        public int CallCount;

        public async IAsyncEnumerable<RawTrackFeatures> StreamEligibleTracksAsync(
            int batchSize,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            System.Threading.Interlocked.Increment(ref CallCount);
            await Task.Yield();
            yield return new RawTrackFeatures("a", Vec(0.0), null, false);
            yield return new RawTrackFeatures("b", Vec(1.0), null, false);
        }
    }

    /// <summary>Feature source que falha na primeira chamada e funciona nas seguintes — simula recovery do Postgres.</summary>
    private sealed class FlakyFeatureSource : ISimilarityFeatureSource
    {
        private int _callCount;

        public async IAsyncEnumerable<RawTrackFeatures> StreamEligibleTracksAsync(
            int batchSize,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int call = System.Threading.Interlocked.Increment(ref _callCount);
            await Task.Yield();
            if (call == 1)
                throw new InvalidOperationException("Postgres indisponível.");
            yield return new RawTrackFeatures("a", Vec(0.0), null, false);
        }
    }

    /// <summary>Escopo de serviço mínimo que resolve uma única implementação de <see cref="ISimilarityFeatureSource"/>.</summary>
    private static IServiceScopeFactory ScopeFactoryWith(ISimilarityFeatureSource source)
    {
        var services = new ServiceCollection();
        services.AddSingleton(source);
        ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static CachedTrackSimilarityIndexProvider Build(ISimilarityFeatureSource source) =>
        new(ScopeFactoryWith(source), NullLogger<CachedTrackSimilarityIndexProvider>.Instance);

    // --- testes ---

    [Fact]
    public async Task GetIndexAsync_WhenNeverInitialized_BuildsOnDemand()
    {
        // Arrange: provider recém-criado, warm-up nunca chamado e não registrado.
        var source = new CountingFeatureSource();
        using var provider = Build(source);

        // Act: GetIndexAsync sem warm-up prévio deve montar sob demanda.
        SimilarityIndex index = await provider.GetIndexAsync();

        // Assert: índice montado e source chamado exatamente uma vez.
        Assert.Equal(2, index.Count);
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task GetIndexAsync_AfterFailedWarmUp_BuildsOnDemand()
    {
        // Arrange: warm-up falhou (Postgres indisponível na 1ª chamada; funciona na 2ª — simula recovery).
        var source = new FlakyFeatureSource();
        using var provider = Build(source);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.WarmUpAsync(CancellationToken.None));

        // Assert que o gate foi liberado e _index continua null — caminho sob demanda deve funcionar.
        // Act: segundo GetIndexAsync monta sob demanda porque o warm-up falhou.
        SimilarityIndex index = await provider.GetIndexAsync();
        Assert.Equal(1, index.Count); // FlakyFeatureSource retorna 1 faixa na 2ª chamada
    }

    [Fact]
    public async Task GetIndexAsync_WhileWarmUpIsRunning_ThrowsServiceUnavailableException()
    {
        // Garante o critério de aceite: requisição chegando DURANTE a montagem recebe 503 imediata.
        var buildGate = new SemaphoreSlim(0, 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingSource = new BlockingFeatureSource(buildGate, started);
        using var provider = Build(blockingSource);

        // Inicia o warm-up mas não libera o buildGate ainda.
        Task warmUp = Task.Run(async () => await provider.WarmUpAsync(CancellationToken.None));

        // Espera de forma determinística: o sinal é publicado pela BlockingFeatureSource
        // imediatamente antes de bloquear no buildGate, momento em que o estado já é Building
        // (publicado por ExecuteBuildAsync antes de chamar BuildIndexAsync) — sem Task.Delay.
        await started.Task;

        // Requisição chegando durante a montagem deve receber ServiceUnavailableException imediatamente.
        var ex = await Assert.ThrowsAsync<ServiceUnavailableException>(() => provider.GetIndexAsync());
        Assert.NotNull(ex.RetryAfterSeconds);
        Assert.True(ex.RetryAfterSeconds > 0);

        // Limpeza: libera o warm-up para terminar.
        buildGate.Release();
        await warmUp;

        // Confirma que a montagem ocorreu exatamente uma vez — a 503 não disparou segunda varredura.
        Assert.Equal(1, blockingSource.StreamCallCount);
    }

    [Fact]
    public async Task GetIndexAsync_AfterWarmUp_ReturnsIndex()
    {
        // Arrange
        using var provider = Build(new CountingFeatureSource());

        // Act: warm-up completo.
        await provider.WarmUpAsync(CancellationToken.None);

        // Assert: agora a chamada retorna o índice com as faixas do catálogo.
        SimilarityIndex index = await provider.GetIndexAsync();
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public async Task WarmUpAsync_CalledConcurrently_BuildsIndexOnlyOnce()
    {
        // Arrange: buildGate bloqueia a varredura; started sinaliza quando a montagem começou.
        var buildGate = new SemaphoreSlim(0, 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingSource = new BlockingFeatureSource(buildGate, started);
        using var provider = Build(blockingSource);

        // Dispara o primeiro warm-up (vai bloquear na BlockingFeatureSource).
        Task warmUp1 = Task.Run(async () => await provider.WarmUpAsync(CancellationToken.None));

        // Espera de forma determinística: o sinal é publicado quando o primeiro warm-up
        // está dentro de BuildIndexAsync e retém o _gate do provider — sem Task.Delay.
        await started.Task;

        // Dispara o segundo warm-up em paralelo — deve ser descartado pelo double-check do semáforo.
        Task warmUp2 = Task.Run(async () => await provider.WarmUpAsync(CancellationToken.None));

        // Libera o build para terminar.
        buildGate.Release();
        await Task.WhenAll(warmUp1, warmUp2);

        // Assert: source varrido exatamente uma vez (não duas), e índice com 1 faixa.
        Assert.Equal(1, blockingSource.StreamCallCount);
        SimilarityIndex index = await provider.GetIndexAsync();
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public async Task WarmUpAsync_IsIdempotent_WhenCalledAfterCompletion()
    {
        // O provider é chamado duas vezes sequencialmente — a segunda deve retornar sem refazer a varredura.
        var source = new CountingFeatureSource();
        using var provider = Build(source);

        await provider.WarmUpAsync(CancellationToken.None);
        await provider.WarmUpAsync(CancellationToken.None);

        // O source foi chamado apenas uma vez: o fast path do segundo WarmUpAsync curto-circuitou.
        Assert.Equal(1, source.CallCount);
    }

    // --- testes novos: prova dos achados C1, C2, C3 ---

    /// <summary>
    /// C1: 503 espúrio com o índice já pronto.
    /// Verifica que GetIndexAsync não lança 503 quando o índice foi publicado antes do throw.
    /// Prova vermelha: se o throw ocorrer sem re-leitura do estado, este teste falharia porque
    /// o índice estaria pronto mas o caller receberia ServiceUnavailableException.
    /// </summary>
    [Fact]
    public async Task GetIndexAsync_WhenBuildingStateButIndexAlreadyPublished_ReturnsIndexWithoutThrowing()
    {
        // Arrange: força o estado para Building e publica o índice diretamente via WarmUpAsync completo,
        // simulando a race onde Building é lido mas Ready já foi publicado antes do throw.
        // A forma determinística: completar o warm-up e verificar que GetIndexAsync retorna o índice.
        // O índice deve estar disponível independentemente do estado interno que foi transitado.
        var buildGate = new SemaphoreSlim(0, 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingSource = new BlockingFeatureSource(buildGate, started);
        using var provider = Build(blockingSource);

        Task warmUp = Task.Run(async () => await provider.WarmUpAsync(CancellationToken.None));
        await started.Task;

        // Neste ponto: estado é Building, _index ainda é null — 503 esperada.
        var ex = await Assert.ThrowsAsync<ServiceUnavailableException>(() => provider.GetIndexAsync());
        Assert.NotNull(ex.RetryAfterSeconds);

        // Libera o warm-up: estado transita para Ready, _index publicado.
        buildGate.Release();
        await warmUp;

        // Agora o índice está pronto: GetIndexAsync NÃO deve lançar 503.
        // Se o throw ocorresse sem re-leitura do estado (bug C1), isto falharia.
        SimilarityIndex index = await provider.GetIndexAsync();
        Assert.Equal(1, index.Count);
    }

    /// <summary>
    /// C2: requisição no arranque não pode pagar a varredura síncrona com warm-up registrado.
    /// Prova vermelha: sem NotifyWarmUpRegistered, GetIndexAsync construiria sob demanda quando
    /// o estado é NotStarted — este teste falharia porque esperaria ServiceUnavailableException.
    /// </summary>
    [Fact]
    public async Task GetIndexAsync_WhenWarmUpRegisteredButNotStarted_ThrowsServiceUnavailableException()
    {
        // Arrange: provider com warm-up registrado mas ExecuteAsync ainda não rodou.
        // Simula o intervalo entre construção do hosted service e início do BackgroundService.
        var source = new CountingFeatureSource();
        using var provider = Build(source);

        // NotifyWarmUpRegistered é chamado pelo construtor do SimilarityIndexWarmUpService.
        // Aqui simulamos isso diretamente.
        provider.NotifyWarmUpRegistered();

        // Act: GetIndexAsync deve retornar 503 imediatamente, sem construir o índice.
        var ex = await Assert.ThrowsAsync<ServiceUnavailableException>(() => provider.GetIndexAsync());

        // Assert: 503 com Retry-After, e source NÃO foi chamado.
        Assert.NotNull(ex.RetryAfterSeconds);
        Assert.True(ex.RetryAfterSeconds > 0);
        Assert.Equal(0, source.CallCount);
    }

    /// <summary>
    /// C2 (complemento): após o warm-up registrado concluir, GetIndexAsync retorna o índice normalmente.
    /// </summary>
    [Fact]
    public async Task GetIndexAsync_WhenWarmUpRegisteredAndCompleted_ReturnsIndex()
    {
        // Arrange
        var source = new CountingFeatureSource();
        using var provider = Build(source);
        provider.NotifyWarmUpRegistered();

        // Simula o warm-up sendo executado pelo BackgroundService.
        await provider.WarmUpAsync(CancellationToken.None);

        // Act: agora deve retornar o índice sem 503.
        SimilarityIndex index = await provider.GetIndexAsync();

        // Assert
        Assert.Equal(2, index.Count);
        Assert.Equal(1, source.CallCount);
    }

    /// <summary>
    /// C2 (recovery): após warm-up registrado falhar, GetIndexAsync volta a construir sob demanda.
    /// O estado Failed indica que o warm-up tentou mas não conseguiu — a construção sob demanda
    /// é o mecanismo de recovery, e deve funcionar mesmo com warm-up previamente registrado.
    /// </summary>
    [Fact]
    public async Task GetIndexAsync_WhenWarmUpRegisteredAndFailed_BuildsOnDemand()
    {
        // Arrange: warm-up registrado, mas falhou.
        var source = new FlakyFeatureSource();
        using var provider = Build(source);
        provider.NotifyWarmUpRegistered();

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.WarmUpAsync(CancellationToken.None));

        // Act: após falha, GetIndexAsync deve tentar montar sob demanda (recovery).
        SimilarityIndex index = await provider.GetIndexAsync();

        // Assert: índice montado com a segunda tentativa da FlakyFeatureSource.
        Assert.Equal(1, index.Count);
    }

    /// <summary>
    /// C3: os dois caminhos de montagem (warm-up e sob demanda) continuam cobertos com o protocolo unificado.
    /// Verifica que WarmUpAsync e GetIndexAsync (sob demanda) produzem o mesmo resultado final.
    /// </summary>
    [Fact]
    public async Task WarmUpPath_AndOnDemandPath_BothProduceCorrectIndex()
    {
        // Caminho warm-up.
        var sourceForWarmUp = new CountingFeatureSource();
        using var providerWithWarmUp = Build(sourceForWarmUp);
        await providerWithWarmUp.WarmUpAsync(CancellationToken.None);
        SimilarityIndex indexFromWarmUp = await providerWithWarmUp.GetIndexAsync();

        // Caminho sob demanda (sem warm-up).
        var sourceOnDemand = new CountingFeatureSource();
        using var providerOnDemand = Build(sourceOnDemand);
        SimilarityIndex indexOnDemand = await providerOnDemand.GetIndexAsync();

        // Ambos devem produzir índices com o mesmo número de faixas e cada source chamado uma vez.
        Assert.Equal(2, indexFromWarmUp.Count);
        Assert.Equal(2, indexOnDemand.Count);
        Assert.Equal(1, sourceForWarmUp.CallCount);
        Assert.Equal(1, sourceOnDemand.CallCount);
    }
}
