using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// A fração de teste fora de [0,05; 0,50] é erro de domínio, não clamp silencioso: um split com 1% ou 90% das
/// faixas não é um split, é um engano — e o consumidor precisa saber (o handler da query clampa antes, de
/// propósito; o value object é a última linha de defesa).
/// </summary>
public sealed class TrainingDatasetSplitOptionsTests
{
    [Theory]
    [InlineData(0.04)]
    [InlineData(0.51)]
    [InlineData(0.90)]
    [InlineData(double.NaN)]
    public void Create_WithTestFractionOutOfRange_Throws(double testFraction)
        => Assert.Throws<DomainException>(
            () => TrainingDatasetSplitOptions.Create(seed: 1, testFraction: testFraction));

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.2)]
    [InlineData(0.5)]
    public void Create_WithTestFractionInRange_Succeeds(double testFraction)
    {
        TrainingDatasetSplitOptions options = TrainingDatasetSplitOptions.Create(seed: 7, testFraction: testFraction);

        Assert.Equal(7, options.Seed);
        Assert.Equal(testFraction, options.TestFraction);
        Assert.Equal(ImputedFeaturePolicy.ExcludeImputed, options.ImputedFeaturePolicy);
    }

    [Fact]
    public void Options_WithSameValues_AreEqual_ByValue()
    {
        TrainingDatasetSplitOptions a = TrainingDatasetSplitOptions.Create(20260730, 0.2);
        TrainingDatasetSplitOptions b = TrainingDatasetSplitOptions.Create(20260730, 0.2);

        Assert.Equal(a, b);
    }
}
