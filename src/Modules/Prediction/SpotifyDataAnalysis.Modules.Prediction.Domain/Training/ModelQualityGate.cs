namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// A régua que decide se um modelo aprendeu alguma coisa: ele precisa reduzir o MAE em pelo menos
/// <see cref="RequiredMaeImprovement"/> sobre o baseline de prever sempre a média do treino.
///
/// <para>O gate é <b>relativo ao baseline</b>, e não um limiar absoluto de MAE, porque só a comparação
/// responde "o modelo aprendeu?" sem depender de conhecer a variância do alvo de antemão — e ele continua
/// válido se o catálogo mudar. Um "MAE &lt; 12" ficaria refém de um número inventado.</para>
///
/// <para><b>R² não entra no gate</b>, de propósito: com alvo comprovadamente ruidoso (popularidade depende de
/// marketing e playlist editorial, não só de áudio), R² baixo é o resultado esperado, não defeito. Ele é
/// reportado como informação.</para>
/// </summary>
public static class ModelQualityGate
{
    /// <summary>Redução mínima do MAE sobre o baseline para o modelo ser considerado útil: 5%.</summary>
    public const double RequiredMaeImprovement = 0.05;

    /// <summary>Fração do MAE do baseline que o modelo não pode ultrapassar.</summary>
    public const double MaxMaeRatio = 1 - RequiredMaeImprovement;

    /// <summary>
    /// Avalia o modelo contra o baseline. Baseline com MAE zero (alvo constante) reprova qualquer modelo que
    /// não seja igualmente perfeito: não há margem percentual a extrair de zero, e passar nesse caso seria
    /// aprovar por divisão por zero.
    /// </summary>
    public static ModelQualityVerdict Evaluate(RegressionMetrics model, RegressionMetrics baseline)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(baseline);

        if (baseline.MeanAbsoluteError <= 0)
        {
            return new ModelQualityVerdict(
                model.MeanAbsoluteError <= 0,
                MaeImprovement: 0,
                RequiredMaeImprovement);
        }

        double improvement = 1 - (model.MeanAbsoluteError / baseline.MeanAbsoluteError);

        return new ModelQualityVerdict(
            model.MeanAbsoluteError <= baseline.MeanAbsoluteError * MaxMaeRatio,
            improvement,
            RequiredMaeImprovement);
    }
}

/// <summary>
/// O veredito do gate, com o número que o sustenta — para o resultado dizer <b>por quanto</b> passou ou
/// falhou, e não só que passou.
/// </summary>
/// <param name="Passed">Se o modelo bateu o baseline pela margem exigida.</param>
/// <param name="MaeImprovement">
/// Redução fracionária do MAE sobre o baseline. Negativo significa que o modelo é PIOR que a média.
/// </param>
/// <param name="RequiredMaeImprovement">A margem exigida, ecoada para o resultado ser autoexplicativo.</param>
public sealed record ModelQualityVerdict(
    bool Passed,
    double MaeImprovement,
    double RequiredMaeImprovement);
