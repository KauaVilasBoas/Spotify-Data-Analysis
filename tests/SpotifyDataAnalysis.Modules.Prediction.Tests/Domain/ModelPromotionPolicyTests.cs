using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// A política de promoção (DP-2): passar o gate E não piorar o MAE corrente. É a regra que impede o retreino
/// agendado do E6.1 de publicar sozinho um modelo degradado.
/// </summary>
public sealed class ModelPromotionPolicyTests
{
    private static RegressionMetrics WithMae(double mae) => new(RSquared: 0, mae, RootMeanSquaredError: mae);

    private static readonly RegressionMetrics Baseline = WithMae(20);

    [Fact]
    public void WithoutACurrentVersion_AnyModelThatPassesTheGateIsPromoted()
    {
        // Modelo útil é melhor que modelo nenhum.
        ModelPromotionDecision decision =
            ModelPromotionPolicy.Decide(WithMae(15), Baseline, currentMetrics: null);

        Assert.True(decision.ShouldPromote);
    }

    [Fact]
    public void WithoutACurrentVersion_AModelThatFailsTheGateIsNotPromoted()
    {
        // 20 → 19.5 é só 2,5% de ganho: abaixo da margem.
        ModelPromotionDecision decision =
            ModelPromotionPolicy.Decide(WithMae(19.5), Baseline, currentMetrics: null);

        Assert.False(decision.ShouldPromote);
        Assert.Contains("não bateu o baseline", decision.Reason);
    }

    [Fact]
    public void APassingModelThatIsWorseThanTheCurrentOne_IsNotPromoted()
    {
        // Este é o caso que a política existe para impedir: passa no gate, mas regride em relação à corrente.
        ModelPromotionDecision decision =
            ModelPromotionPolicy.Decide(WithMae(16), Baseline, currentMetrics: WithMae(15));

        Assert.False(decision.ShouldPromote);
        Assert.Contains("pior que o da corrente", decision.Reason);
    }

    [Fact]
    public void APassingModelThatImprovesOnTheCurrentOne_IsPromoted()
    {
        ModelPromotionDecision decision =
            ModelPromotionPolicy.Decide(WithMae(14), Baseline, currentMetrics: WithMae(15));

        Assert.True(decision.ShouldPromote);
    }

    [Fact]
    public void APassingModelThatTiesTheCurrentOne_IsPromoted()
    {
        // Empate promove: o modelo novo foi treinado com dados mais recentes, e não é pior.
        ModelPromotionDecision decision =
            ModelPromotionPolicy.Decide(WithMae(15), Baseline, currentMetrics: WithMae(15));

        Assert.True(decision.ShouldPromote);
    }

    [Fact]
    public void AFailingModelIsNeverPromoted_EvenIfBetterThanTheCurrentOne()
    {
        // A corrente é ruim, a candidata é menos ruim — mas nenhuma das duas aprendeu. Não publica.
        ModelPromotionDecision decision =
            ModelPromotionPolicy.Decide(WithMae(19.5), Baseline, currentMetrics: WithMae(19.9));

        Assert.False(decision.ShouldPromote);
    }

    [Fact]
    public void TheDecisionCarriesTheGateVerdict_SoTheResultExplainsItself()
    {
        ModelPromotionDecision decision =
            ModelPromotionPolicy.Decide(WithMae(15), Baseline, currentMetrics: null);

        Assert.True(decision.Gate.Passed);
        Assert.Equal(0.25, decision.Gate.MaeImprovement, precision: 10);
    }
}
