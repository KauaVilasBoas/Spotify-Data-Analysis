using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// A varredura kNN visita todas as faixas do índice mas só retém N: a alocação de uma chamada tem de depender do
/// top-N, não do tamanho do catálogo. Mede bytes alocados na thread (<see cref="GC.GetAllocatedBytesForCurrentThread"/>)
/// sobre um índice sintético determinístico, então roda no CI sem Postgres.
/// </summary>
public sealed class SimilarityIndexAllocationTests
{
    private const int TopN = 10;
    private const int MeasuredCalls = 20;
    private const long CeilingBytesPerCall = 16 * 1024;

    private static SimilarityIndex BuildIndex(int size)
    {
        var random = new Random(4313);
        var tracks = new List<RawTrackFeatures>(size);

        for (int i = 0; i < size; i++)
        {
            SimilarityFeatureVector raw = SimilarityFeatureVector.Create(
            [
                random.NextDouble(), random.NextDouble(), random.NextDouble(), 60 + (random.NextDouble() * 120),
                random.NextDouble(), random.NextDouble(), random.NextDouble(), random.NextDouble(),
                -30 + (random.NextDouble() * 30)
            ]);

            tracks.Add(new RawTrackFeatures($"t{i:D6}", raw, Genre: null, IsImputed: false));
        }

        return SimilarityIndex.Build(tracks);
    }

    private static double AllocatedBytesPerCall(SimilarityIndex index)
    {
        index.FindNearestTo("t000000", TopN);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < MeasuredCalls; i++)
            index.FindNearestTo("t000000", TopN);

        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)MeasuredCalls;
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(20_000)]
    public void FindNearestTo_AllocationDoesNotGrowWithIndexSize(int indexSize)
    {
        double bytesPerCall = AllocatedBytesPerCall(BuildIndex(indexSize));

        Assert.True(
            bytesPerCall < CeilingBytesPerCall,
            $"Índice de {indexSize} faixas alocou {bytesPerCall:0} B por varredura; teto {CeilingBytesPerCall} B.");
    }
}
