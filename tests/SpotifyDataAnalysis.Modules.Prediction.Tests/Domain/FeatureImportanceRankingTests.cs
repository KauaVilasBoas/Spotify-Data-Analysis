using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// A regra de agregação e ordenação do ranking de importância (E3.6), com números conhecidos e sem ML.NET —
/// que é justamente por que ela mora no domínio: medir é da Infrastructure, o que se faz com a medição é regra.
/// </summary>
public sealed class FeatureImportanceRankingTests
{
    private static FeatureSlotImportance Slot(
        string feature, double rSquaredDrop, double deviation = 0, double maeIncrease = 0) =>
        new(feature, new MetricDelta(rSquaredDrop, deviation), new MetricDelta(maeIncrease, deviation));

    [Fact]
    public void Build_CollapsesEverySlotOfABlockIntoASingleLine()
    {
        IReadOnlyList<FeatureImportance> ranking = FeatureImportanceRanking.Build(
        [
            Slot("Genre", 0.01),
            Slot("Genre", 0.02),
            Slot("Genre", 0.03),
            Slot("Danceability", 0.10)
        ]);

        FeatureImportance genre = Assert.Single(ranking, feature => feature.Feature == "Genre");

        Assert.Equal(3, genre.SlotCount);
        Assert.Equal(0.06, genre.RSquaredDrop.Mean, precision: 10);
    }

    /// <summary>
    /// Cada slot é um sorteio independente, então as variâncias somam e os desvios não: somar desvios direto
    /// publicaria uma incerteza maior do que a medição sustenta.
    /// </summary>
    [Fact]
    public void Build_CombinesDispersionInQuadrature_NotByPlainSum()
    {
        IReadOnlyList<FeatureImportance> ranking = FeatureImportanceRanking.Build(
        [
            Slot("Genre", 0.01, deviation: 3),
            Slot("Genre", 0.01, deviation: 4)
        ]);

        Assert.Equal(5, ranking[0].RSquaredDrop.StandardDeviation, precision: 10);
    }

    [Fact]
    public void Build_KeepsAScalarFeatureAsASingleSlot_WithItsDispersionUntouched()
    {
        IReadOnlyList<FeatureImportance> ranking = FeatureImportanceRanking.Build(
            [Slot("Tempo", 0.04, deviation: 0.5)]);

        Assert.Equal(1, ranking[0].SlotCount);
        Assert.Equal(0.5, ranking[0].RSquaredDrop.StandardDeviation, precision: 10);
    }

    [Fact]
    public void Build_OrdersByRSquaredDropDescending()
    {
        IReadOnlyList<FeatureImportance> ranking = FeatureImportanceRanking.Build(
        [
            Slot("Tempo", 0.01),
            Slot("Danceability", 0.30),
            Slot("Liveness", -0.02),
            Slot("Energy", 0.12)
        ]);

        Assert.Equal(
            ["Danceability", "Energy", "Tempo", "Liveness"],
            ranking.Select(feature => feature.Feature));
    }

    /// <summary>
    /// Empate sem critério final faria a ordem depender da enumeração do agrupamento — dois treinos com a
    /// mesma semente publicariam rankings diferentes.
    /// </summary>
    [Fact]
    public void Build_BreaksTiesDeterministically_SoTheRankingDoesNotShuffleBetweenRuns()
    {
        IReadOnlyList<FeatureImportance> ranking = FeatureImportanceRanking.Build(
            [Slot("Zeta", 0.05), Slot("Alfa", 0.05)]);

        Assert.Equal(["Alfa", "Zeta"], ranking.Select(feature => feature.Feature));
    }

    /// <summary>
    /// A ordem é invariante da versão, e invariante que depende de quem chama não é invariante.
    /// </summary>
    [Fact]
    public void Register_PublishesTheRankingOrdered_EvenWhenTheCallerHandsItShuffled()
    {
        ModelVersion version = ModelVersion.Register(
            DateTime.UtcNow,
            "FastTree",
            ["Danceability", "Energy"],
            [
                new FeatureImportance("Energy", 1, new MetricDelta(0.02, 0), new MetricDelta(0.4, 0)),
                new FeatureImportance("Danceability", 1, new MetricDelta(0.31, 0), new MetricDelta(4.1, 0))
            ],
            seed: 42,
            testFraction: 0.2,
            trainingSampleCount: 800,
            testSampleCount: 200,
            trainedOnImputed: false,
            new RegressionMetrics(0.15, 15.1, 18.9),
            new RegressionMetrics(0, 17.2, 20.5),
            [1, 2, 3],
            "abc123");

        Assert.Equal(
            ["Danceability", "Energy"],
            version.FeatureImportance.Select(feature => feature.Feature));
    }

    /// <summary>
    /// Versões anteriores ao E3.6 não têm medição, e lista vazia é "não foi medido" — nunca "nada importa".
    /// </summary>
    [Fact]
    public void Register_WithoutMeasuredImportance_PublishesAnEmptyRanking()
    {
        ModelVersion version = ModelVersion.Register(
            DateTime.UtcNow,
            "FastTree",
            ["Danceability"],
            featureImportance: null,
            seed: 42,
            testFraction: 0.2,
            trainingSampleCount: 800,
            testSampleCount: 200,
            trainedOnImputed: false,
            new RegressionMetrics(0.15, 15.1, 18.9),
            new RegressionMetrics(0, 17.2, 20.5),
            [1, 2, 3],
            "abc123");

        Assert.Empty(version.FeatureImportance);
    }
}
