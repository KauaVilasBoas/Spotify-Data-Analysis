using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// A régua do gate (DP-2): reduzir o MAE em pelo menos 5% sobre o baseline da média. Estes testes travam a
/// régua em si; que o pipeline real a atravesse é o <c>PopularityModelQualityGateTests</c>.
/// </summary>
public sealed class ModelQualityGateTests
{
    private static RegressionMetrics WithMae(double mae) => new(RSquared: 0, mae, RootMeanSquaredError: mae);

    [Fact]
    public void Passes_WhenTheModelBeatsTheBaselineByMoreThanTheRequiredMargin()
    {
        ModelQualityVerdict verdict = ModelQualityGate.Evaluate(WithMae(8), WithMae(10));

        Assert.True(verdict.Passed);
        Assert.Equal(0.20, verdict.MaeImprovement, precision: 10);
    }

    [Fact]
    public void Passes_ExactlyAtTheThreshold()
    {
        // 5% cravado passa: a régua é "pelo menos 5%".
        ModelQualityVerdict verdict = ModelQualityGate.Evaluate(WithMae(9.5), WithMae(10));

        Assert.True(verdict.Passed);
        Assert.Equal(ModelQualityGate.RequiredMaeImprovement, verdict.MaeImprovement, precision: 10);
    }

    [Fact]
    public void Fails_WhenTheGainIsRealButSmallerThanTheMargin()
    {
        // 2% de ganho é ruído, não aprendizado — e é exatamente o caso que um gate frouxo deixaria passar.
        ModelQualityVerdict verdict = ModelQualityGate.Evaluate(WithMae(9.8), WithMae(10));

        Assert.False(verdict.Passed);
        Assert.Equal(0.02, verdict.MaeImprovement, precision: 10);
    }

    [Fact]
    public void Fails_WhenTheModelIsWorseThanTheMean_AndSaysByHowMuch()
    {
        ModelQualityVerdict verdict = ModelQualityGate.Evaluate(WithMae(12), WithMae(10));

        Assert.False(verdict.Passed);
        Assert.True(verdict.MaeImprovement < 0);
    }

    [Fact]
    public void WithPerfectBaseline_OnlyAPerfectModelPasses()
    {
        // MAE zero no baseline não deixa margem percentual a extrair; aprovar aqui seria dividir por zero.
        Assert.False(ModelQualityGate.Evaluate(WithMae(0.1), WithMae(0)).Passed);
        Assert.True(ModelQualityGate.Evaluate(WithMae(0), WithMae(0)).Passed);
    }

    [Fact]
    public void Verdict_EchoesTheRequiredMargin_SoTheResultExplainsItself()
    {
        ModelQualityVerdict verdict = ModelQualityGate.Evaluate(WithMae(8), WithMae(10));

        Assert.Equal(ModelQualityGate.RequiredMaeImprovement, verdict.RequiredMaeImprovement);
    }
}
