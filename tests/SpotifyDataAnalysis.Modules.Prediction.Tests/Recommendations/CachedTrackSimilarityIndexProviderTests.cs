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
///   <item>Nunca iniciada (warm-up não rodou) → monta sob demanda.</item>
///   <item>Warm-up falhou → monta sob demanda na próxima chamada.</item>
///   <item>Duas chamadas concorrentes de warm-up não disparam duas varreduras.</item>
///   <item>Após montado → retorna o índice correto.</item>
/// </list>
/// </summary>
public sealed class CachedTrackSimilarityIndexProviderTests
{
    private static SimilarityFeatureVector Vec(double seed) =>
        SimilarityFeatureVector.Create(
            Enumerable.Range(1, SimilarityFeatures.Dimension).Select(i => i + seed).ToArray());

    // --- stubs ---

    /// <summary>Feature source que libera apenas após um semáforo externo ser sinalizado — controla o timing.</summary>
    private sealed class BlockingFeatureSource : ISimilarityFeatureSource
    {
        private readonly SemaphoreSlim _gate;

        public BlockingFeatureSource(SemaphoreSlim gate) => _gate = gate;

        public async IAsyncEnumerable<RawTrackFeatures> StreamEligibleTracksAsync(
            int batchSize,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
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
        // Arrange: provider recém-criado, warm-up nunca chamado.
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
        using var provider = Build(new BlockingFeatureSource(buildGate));

        // Inicia o warm-up mas não libera o build ainda.
        Task warmUp = Task.Run(async () => await provider.WarmUpAsync(CancellationToken.None));

        // Aguarda o warm-up estar segurando o gate de construção.
        await Task.Delay(50);

        // Requisição chegando durante a montagem deve receber ServiceUnavailableException imediatamente.
        var ex = await Assert.ThrowsAsync<ServiceUnavailableException>(() => provider.GetIndexAsync());
        Assert.NotNull(ex.RetryAfterSeconds);
        Assert.True(ex.RetryAfterSeconds > 0);

        // Limpeza: libera o warm-up para terminar.
        buildGate.Release();
        await warmUp;
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
        // Arrange: dois gates — um bloqueia a varredura, o outro sincroniza o início das duas tarefas.
        var buildGate = new SemaphoreSlim(0, 1);   // liberado para deixar o build terminar
        var blockingSource = new BlockingFeatureSource(buildGate);

        using var provider = Build(blockingSource);

        // Dispara o primeiro warm-up (vai bloquear na BlockingFeatureSource).
        Task warmUp1 = Task.Run(async () => await provider.WarmUpAsync(CancellationToken.None));

        // Aguarda um instante para que warmUp1 já esteja dentro do WaitAsync do gate.
        await Task.Delay(50);

        // Dispara o segundo warm-up em paralelo — deve ser descartado pelo double-check do semáforo.
        Task warmUp2 = Task.Run(async () => await provider.WarmUpAsync(CancellationToken.None));

        // Libera o build para terminar.
        buildGate.Release();
        await Task.WhenAll(warmUp1, warmUp2);

        // Assert: índice com exatamente 1 faixa (BlockingFeatureSource), sem duplicata.
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
}
