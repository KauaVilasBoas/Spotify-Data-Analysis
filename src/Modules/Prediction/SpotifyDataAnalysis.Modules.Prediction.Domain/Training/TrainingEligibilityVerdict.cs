namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// O veredito da <see cref="TrainingEligibilitySpecification"/> sobre uma faixa: entra, ou fica de fora
/// <b>por um motivo nomeado</b>. Não existe estado "fora sem motivo" — é o que permite às estatísticas do
/// dataset fecharem a conta entre o total do catálogo e o total treinável.
/// </summary>
public readonly record struct TrainingEligibilityVerdict
{
    private TrainingEligibilityVerdict(TrainingExclusionReason? exclusionReason)
        => ExclusionReason = exclusionReason;

    /// <summary>Motivo da exclusão; nulo quando a faixa é elegível.</summary>
    public TrainingExclusionReason? ExclusionReason { get; }

    /// <summary>Se a faixa entra no dataset de treino.</summary>
    public bool IsEligible => ExclusionReason is null;

    /// <summary>Veredito de aprovação.</summary>
    public static TrainingEligibilityVerdict Eligible() => new(null);

    /// <summary>Veredito de exclusão pelo motivo informado.</summary>
    public static TrainingEligibilityVerdict ExcludedBecause(TrainingExclusionReason reason) => new(reason);
}
