using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Models;

/// <summary>
/// Decide se uma versão recém-treinada deve virar corrente (DP-2).
///
/// <para>Duas condições, e as duas precisam valer: a versão nova tem de <b>passar o gate de qualidade</b>
/// (bater o baseline pela margem exigida) e <b>não piorar o MAE</b> da versão corrente. A primeira impede
/// publicar um modelo que não aprendeu; a segunda impede que um retreino sobre dados degradados — o cenário do
/// retreino agendado do E6.1 — substitua sozinho um modelo melhor por um pior.</para>
///
/// <para>Reprovar não descarta a versão: ela fica registrada como candidata, auditável, e a corrente
/// permanece. Perder o registro do treino ruim seria perder a evidência de que ele aconteceu.</para>
/// </summary>
public static class ModelPromotionPolicy
{
    /// <summary>
    /// Avalia a promoção. <paramref name="currentMetrics"/> nulo significa que ainda não há versão corrente —
    /// nesse caso basta passar o gate, porque qualquer modelo útil é melhor que modelo nenhum.
    /// </summary>
    public static ModelPromotionDecision Decide(
        RegressionMetrics candidateMetrics,
        RegressionMetrics candidateBaseline,
        RegressionMetrics? currentMetrics)
    {
        ArgumentNullException.ThrowIfNull(candidateMetrics);
        ArgumentNullException.ThrowIfNull(candidateBaseline);

        ModelQualityVerdict gate = ModelQualityGate.Evaluate(candidateMetrics, candidateBaseline);

        if (!gate.Passed)
        {
            return new ModelPromotionDecision(
                false,
                gate,
                $"A versão não bateu o baseline pela margem exigida (melhora de {gate.MaeImprovement:P2}, " +
                $"exigido {gate.RequiredMaeImprovement:P2}).");
        }

        if (currentMetrics is not null && candidateMetrics.MeanAbsoluteError > currentMetrics.MeanAbsoluteError)
        {
            return new ModelPromotionDecision(
                false,
                gate,
                $"A versão passou no gate mas tem MAE pior que o da corrente " +
                $"({candidateMetrics.MeanAbsoluteError:F3} contra {currentMetrics.MeanAbsoluteError:F3}).");
        }

        return new ModelPromotionDecision(true, gate, "A versão passou no gate e não piorou o MAE corrente.");
    }
}

/// <summary>
/// O resultado da política, com a razão em texto — para o endpoint de treino dizer <b>por que</b> a versão foi
/// ou não publicada, em vez de devolver um booleano mudo.
/// </summary>
/// <param name="ShouldPromote">Se a versão deve virar corrente.</param>
/// <param name="Gate">O veredito do gate de qualidade que sustentou a decisão.</param>
/// <param name="Reason">Explicação legível da decisão.</param>
public sealed record ModelPromotionDecision(
    bool ShouldPromote,
    ModelQualityVerdict Gate,
    string Reason);
