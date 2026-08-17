using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Recommendations;

/// <summary>
/// Cache SINGLETON do <see cref="SimilarityIndex"/> do catálogo (DP-D: on-the-fly com índice em memória). Monta o
/// índice na primeira consulta — varrendo <see cref="ISimilarityFeatureSource"/> e aprendendo μ/σ — e o reusa nas
/// seguintes. Mesmo princípio do <c>CurrentModelCache</c> do E3.5: o custo é de CARGA, não de consulta; refazer a
/// varredura de ~90k faixas por request seria absurdo, guardar ~7 MB não é.
///
/// <para><b>Singleton lendo de um source scoped:</b> o <see cref="ISimilarityFeatureSource"/> depende do
/// <c>DbConnectionFactory</c> (scoped, per-request), então um singleton não pode injetá-lo direto. A montagem
/// abre um scope próprio via <see cref="IServiceScopeFactory"/> — o padrão canônico para um serviço de vida longa
/// consumir um colaborador de vida curta sem capturar uma conexão além do necessário.</para>
///
/// <para>O <see cref="SemaphoreSlim"/> serializa só a PRIMEIRA montagem: consultas concorrentes no arranque
/// aguardam a mesma varredura em vez de dispararem várias. Depois de montado, o caminho quente é um retorno do
/// campo já publicado, sem trava.</para>
/// </summary>
internal sealed class CachedTrackSimilarityIndexProvider : ITrackSimilarityIndexProvider
{
    /// <summary>
    /// Lote de leitura da varredura de montagem. Grande porque é uma varredura única do catálogo inteiro e um
    /// lote maior reduz idas ao banco; ainda dentro do teto do source, que limita o pico de memória do transporte.
    /// </summary>
    internal const int IndexBuildBatchSize = 20_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CachedTrackSimilarityIndexProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile SimilarityIndex? _index;

    public CachedTrackSimilarityIndexProvider(
        IServiceScopeFactory scopeFactory,
        ILogger<CachedTrackSimilarityIndexProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<SimilarityIndex> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        SimilarityIndex? index = _index;
        if (index is not null)
            return index;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            index = _index;
            if (index is not null)
                return index;

            index = await BuildIndexAsync(cancellationToken);
            _index = index;

            return index;
        }
        finally
        {
            _gate.Release();
        }
    }

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

        _logger.LogInformation(
            "Índice de similaridade montado: {TrackCount} faixas em {BuildMs:F0} ms (pico aprox. de memória do índice: {FootprintMb:F1} MB).",
            index.Count, buildDuration.TotalMilliseconds, indexFootprintMb);

        return index;
    }
}
