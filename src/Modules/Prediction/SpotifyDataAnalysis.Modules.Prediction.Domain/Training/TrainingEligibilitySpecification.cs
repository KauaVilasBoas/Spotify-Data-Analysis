namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// A regra que decide quem entra no dataset de treino — o coração do E3.1. Specification explícita e testável
/// em vez de um <c>WHERE</c> perdido dentro de uma query: a elegibilidade é regra de negócio do épico de
/// predição, não detalhe do SQL, e o SQL do read-side é uma projeção burra das colunas.
///
/// <para>A avaliação é em cascata e a ordem importa, porque o primeiro motivo encontrado é o motivo relatado:
/// <list type="number">
///   <item>sem popularidade → não há alvo;</item>
///   <item>sem audio-features (jsonb nulo) → a faixa nunca casou com o dataset externo;</item>
///   <item>audio-features incompletas → alguma grandeza numérica falta no jsonb;</item>
///   <item>features imputadas, quando a política em vigor é <see cref="ImputedFeaturePolicy.ExcludeImputed"/>.</item>
/// </list>
/// Os três primeiros são estruturais e inegociáveis; o quarto é <b>política</b> — daí ser um parâmetro e não
/// um <c>if</c> escondido.</para>
/// </summary>
public sealed class TrainingEligibilitySpecification
{
    private readonly ImputedFeaturePolicy _imputedFeaturePolicy;

    public TrainingEligibilitySpecification(
        ImputedFeaturePolicy imputedFeaturePolicy = ImputedFeaturePolicy.ExcludeImputed)
        => _imputedFeaturePolicy = imputedFeaturePolicy;

    /// <summary>Política de imputação em vigor nesta instância.</summary>
    public ImputedFeaturePolicy ImputedFeaturePolicy => _imputedFeaturePolicy;

    /// <summary>Julga uma faixa candidata e devolve o veredito com o motivo, quando houver exclusão.</summary>
    public TrainingEligibilityVerdict Evaluate(TrackTrainingCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Popularity is null)
            return TrainingEligibilityVerdict.ExcludedBecause(TrainingExclusionReason.MissingPopularity);

        if (!candidate.HasAudioFeatures)
            return TrainingEligibilityVerdict.ExcludedBecause(TrainingExclusionReason.MissingAudioFeatures);

        if (!candidate.HasCompleteAudioFeatures)
            return TrainingEligibilityVerdict.ExcludedBecause(TrainingExclusionReason.IncompleteAudioFeatures);

        if (candidate.IsImputed && _imputedFeaturePolicy == ImputedFeaturePolicy.ExcludeImputed)
            return TrainingEligibilityVerdict.ExcludedBecause(TrainingExclusionReason.ImputedAudioFeatures);

        return TrainingEligibilityVerdict.Eligible();
    }
}
