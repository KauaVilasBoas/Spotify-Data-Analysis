using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// O handler de diagnóstico interno (E4.1): traduz o resultado do motor kNN para o DTO, distingue "semente no
/// índice" de "fora", e cronometra a varredura. Testado contra um índice real montado de um catálogo pequeno
/// (via um provider fake) — o motor já tem seus próprios testes, aqui o foco é o comportamento do handler.
/// </summary>
public sealed class GetSimilarTracksQueryHandlerTests
{
    private static SimilarityFeatureVector Raw(double seed) =>
        SimilarityFeatureVector.Create(Enumerable.Range(1, SimilarityFeatures.Dimension).Select(i => i + seed).ToArray());

    private static ITrackSimilarityIndexProvider ProviderWith(params RawTrackFeatures[] tracks) =>
        new StubIndexProvider(SimilarityIndex.Build(tracks));

    [Fact]
    public async Task Handle_KnownSeed_ReturnsNeighborsWithoutTheSeed()
    {
        ITrackSimilarityIndexProvider provider = ProviderWith(
            new RawTrackFeatures("seed", Raw(0.0), false),
            new RawTrackFeatures("a", Raw(0.1), false),
            new RawTrackFeatures("b", Raw(5.0), false));
        var handler = new GetSimilarTracksQueryHandler(provider);

        SimilarTracksResult result = await handler.HandleAsync(new GetSimilarTracksQuery("seed", 10));

        Assert.True(result.SeedFound);
        Assert.Equal(3, result.IndexedTrackCount);
        Assert.DoesNotContain(result.Neighbors, n => n.TrackId == "seed");
        Assert.True(result.ScanLatencyMs >= 0);
    }

    [Fact]
    public async Task Handle_UnknownSeed_ReportsSeedNotFoundWithEmptyNeighbors()
    {
        ITrackSimilarityIndexProvider provider = ProviderWith(
            new RawTrackFeatures("a", Raw(0.1), false),
            new RawTrackFeatures("b", Raw(5.0), false));
        var handler = new GetSimilarTracksQueryHandler(provider);

        SimilarTracksResult result = await handler.HandleAsync(new GetSimilarTracksQuery("ghost", 10));

        Assert.False(result.SeedFound);
        Assert.Empty(result.Neighbors);
    }

    [Fact]
    public async Task Handle_TopNAboveMaximum_IsClamped()
    {
        var tracks = Enumerable.Range(0, 10)
            .Select(i => new RawTrackFeatures($"t{i}", Raw(i), false))
            .ToArray();
        var handler = new GetSimilarTracksQueryHandler(ProviderWith(tracks));

        SimilarTracksResult result = await handler.HandleAsync(
            new GetSimilarTracksQuery("t0", GetSimilarTracksQuery.MaximumTopN + 500));

        // 10 faixas menos a semente = 9 vizinhos possíveis; o clamp evita pedido absurdo, não inventa vizinhos.
        Assert.Equal(9, result.Neighbors.Count);
    }

    private sealed class StubIndexProvider : ITrackSimilarityIndexProvider
    {
        private readonly SimilarityIndex _index;

        public StubIndexProvider(SimilarityIndex index) => _index = index;

        public Task<SimilarityIndex> GetIndexAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_index);
    }
}
