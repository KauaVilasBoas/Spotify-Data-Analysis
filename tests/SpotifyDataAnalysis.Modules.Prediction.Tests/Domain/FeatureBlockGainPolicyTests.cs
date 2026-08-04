using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// A régua do E3.3 que decide se um bloco de features "paga o próprio custo": só permanece no campeão o bloco
/// que reduzir o MAE em ≥ 2% sobre o conjunto sem ele. É aritmética pura sobre métricas — testável sem ML.NET,
/// que é justamente por isso que a decisão vive no domínio e não no pipeline.
/// </summary>
public sealed class FeatureBlockGainPolicyTests
{
    private static RegressionMetrics Mae(double mae) => new(RSquared: 0.1, MeanAbsoluteError: mae, RootMeanSquaredError: mae + 1);

    [Fact]
    public void Measure_WhenBlockReducesMaeAboveThreshold_Pays()
    {
        // Base MAE 15,168 → 2% seria 14,865. Um bloco que leva a 14,80 paga.
        FeatureBlockGain gain = FeatureBlockGainPolicy.Measure("+A", Mae(15.168), Mae(14.80));

        Assert.True(gain.Pays);
        Assert.True(gain.MaeGain >= FeatureBlockGainPolicy.RequiredMaeGain);
    }

    [Fact]
    public void Measure_WhenGainIsJustBelowTwoPercent_DoesNotPay()
    {
        // 15,168 × (1 − 0,02) = 14,86464. Um MAE apenas acima disso não bate a margem — o bloco é removido.
        FeatureBlockGain gain = FeatureBlockGainPolicy.Measure("+B", Mae(15.168), Mae(14.87));

        Assert.False(gain.Pays);
        Assert.True(gain.MaeGain < FeatureBlockGainPolicy.RequiredMaeGain);
    }

    [Fact]
    public void Measure_WhenBlockWorsensMae_ReportsNegativeGain_AndDoesNotPay()
    {
        FeatureBlockGain gain = FeatureBlockGainPolicy.Measure("+A", Mae(15.0), Mae(15.5));

        Assert.False(gain.Pays);
        Assert.True(gain.MaeGain < 0);
    }

    [Fact]
    public void Measure_WhenExactlyAtThreshold_Pays()
    {
        // Fronteira: MAE que dá exatamente 2% de ganho deve permanecer (comparação inclusiva).
        double atThreshold = 15.0 * (1 - FeatureBlockGainPolicy.RequiredMaeGain);

        FeatureBlockGain gain = FeatureBlockGainPolicy.Measure("+A", Mae(15.0), Mae(atThreshold));

        Assert.True(gain.Pays);
    }

    [Fact]
    public void Measure_WhenBaselineMaeIsZero_DoesNotPay()
    {
        // Não há margem percentual a extrair de zero — passar aqui seria aprovar por divisão degenerada.
        FeatureBlockGain gain = FeatureBlockGainPolicy.Measure("+A", Mae(0), Mae(0));

        Assert.False(gain.Pays);
        Assert.Equal(0, gain.MaeGain);
    }

    [Fact]
    public void Measure_WithoutLabel_Throws()
    {
        Assert.Throws<DomainException>(() => FeatureBlockGainPolicy.Measure(" ", Mae(15), Mae(14)));
    }
}
