using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// O ganho medido de um bloco de features sobre a linha de base, na régua do E3.3: um bloco só "paga o próprio
/// custo" se reduzir o MAE em pelo menos <see cref="FeatureBlockGainPolicy.RequiredMaeGain"/> sobre o conjunto
/// sem ele. Esta é a decisão de domínio — aritmética pura sobre métricas —, deliberadamente fora do pipeline do
/// ML.NET para poder ser testada com números conhecidos.
/// </summary>
/// <param name="BlockLabel">Rótulo do bloco avaliado (ex.: <c>+A</c>, <c>+B</c>).</param>
/// <param name="MaeGain">Redução fracionária do MAE sobre a base. Negativo significa que o bloco PIOROU o MAE.</param>
/// <param name="Pays">Se o ganho bate a margem exigida — o bloco entra no campeão apenas quando verdadeiro.</param>
public sealed record FeatureBlockGain(string BlockLabel, double MaeGain, bool Pays);

/// <summary>
/// A régua que decide se um bloco de features do E3.3 merece permanecer no modelo campeão. É relativa (fração
/// do MAE da base), e não um limiar absoluto, porque a pergunta é "este bloco pagou?" — que só a comparação com
/// o conjunto sem ele responde, e que continua válida se o catálogo mudar.
/// </summary>
public static class FeatureBlockGainPolicy
{
    /// <summary>Redução mínima de MAE, sobre o conjunto sem o bloco, para o bloco permanecer: 2% (regra do card).</summary>
    public const double RequiredMaeGain = 0.02;

    /// <summary>
    /// Mede o ganho de um bloco comparando o MAE do conjunto COM o bloco contra o MAE da base. Base com MAE
    /// não-positivo é dado degenerado (não há erro a reduzir) e reprova o bloco: não há margem percentual a
    /// extrair de zero.
    /// </summary>
    /// <exception cref="DomainException">Quando as métricas são nulas.</exception>
    public static FeatureBlockGain Measure(string blockLabel, RegressionMetrics baseline, RegressionMetrics withBlock)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(withBlock);

        if (string.IsNullOrWhiteSpace(blockLabel))
            throw new DomainException("Um bloco de features precisa de um rótulo para figurar na tabela comparativa.");

        if (baseline.MeanAbsoluteError <= 0)
            return new FeatureBlockGain(blockLabel, MaeGain: 0, Pays: false);

        double gain = 1 - (withBlock.MeanAbsoluteError / baseline.MeanAbsoluteError);

        return new FeatureBlockGain(blockLabel, gain, gain >= RequiredMaeGain);
    }
}
