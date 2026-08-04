namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// Serviço de domínio que monta o dataset de treino faixa a faixa: aplica a
/// <see cref="TrainingEligibilitySpecification"/>, contabiliza cada exclusão pelo motivo e distribui as
/// amostras aprovadas pela <see cref="ITrainTestSplitStrategy"/>.
///
/// <para>Consome as faixas <b>uma a uma</b> (<see cref="Accept"/>), de propósito: quem alimenta pode ler o
/// catálogo em lotes paginados sem nunca ter o resultado completo do banco na mão, e a regra de negócio não
/// muda por causa disso. É o mesmo motor por trás do endpoint de estatísticas e do treino do E3.2 — um só
/// caminho, então o número que o diagnóstico reporta é literalmente o dataset que o treino vai receber.</para>
/// </summary>
public sealed class TrainingDatasetAssembler
{
    private readonly TrainingEligibilitySpecification _eligibility;
    private readonly ITrainTestSplitStrategy _splitStrategy;
    private readonly TrainingDatasetSplitOptions _options;

    private readonly List<TrackTrainingSample> _trainingSamples = [];
    private readonly List<TrackTrainingSample> _testSamples = [];

    private long _totalTracks;
    private long _excludedMissingPopularity;
    private long _excludedMissingAudioFeatures;
    private long _excludedIncompleteAudioFeatures;
    private long _eligibleWithMeasuredFeatures;
    private long _eligibleWithImputedFeatures;
    private long _excludedByImputationPolicy;

    public TrainingDatasetAssembler(
        TrainingDatasetSplitOptions options,
        TrainingEligibilitySpecification eligibility,
        ITrainTestSplitStrategy splitStrategy)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(splitStrategy);

        _options = options;
        _eligibility = eligibility;
        _splitStrategy = splitStrategy;
    }

    /// <summary>Julga uma faixa do catálogo e a incorpora ao dataset, ou a contabiliza como exclusão.</summary>
    public void Accept(TrackTrainingCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        _totalTracks++;

        TrainingEligibilityVerdict verdict = _eligibility.Evaluate(candidate);

        switch (verdict.ExclusionReason)
        {
            case TrainingExclusionReason.MissingPopularity:
                _excludedMissingPopularity++;
                return;

            case TrainingExclusionReason.MissingAudioFeatures:
                _excludedMissingAudioFeatures++;
                return;

            case TrainingExclusionReason.IncompleteAudioFeatures:
                _excludedIncompleteAudioFeatures++;
                return;

            case TrainingExclusionReason.ImputedAudioFeatures:
                _eligibleWithImputedFeatures++;
                _excludedByImputationPolicy++;
                return;

            default:
                CountEligible(candidate);
                AssignToPartition(TrackTrainingSample.FromEligibleCandidate(candidate));
                return;
        }
    }

    /// <summary>Fecha o dataset e devolve as partições com o censo consolidado.</summary>
    public TrainingDatasetPartitions Build() =>
        new(
            _trainingSamples,
            _testSamples,
            new TrainingDatasetStatistics(
                _totalTracks,
                _excludedMissingPopularity,
                _excludedMissingAudioFeatures,
                _excludedIncompleteAudioFeatures,
                _eligibleWithMeasuredFeatures,
                _eligibleWithImputedFeatures,
                _excludedByImputationPolicy,
                _trainingSamples.Count,
                _testSamples.Count,
                _options.ImputedFeaturePolicy,
                _options.Seed,
                _options.TestFraction));

    private void CountEligible(TrackTrainingCandidate candidate)
    {
        if (candidate.IsImputed)
            _eligibleWithImputedFeatures++;
        else
            _eligibleWithMeasuredFeatures++;
    }

    private void AssignToPartition(TrackTrainingSample sample)
    {
        if (_splitStrategy.AssignPartition(sample) == DatasetPartition.Test)
            _testSamples.Add(sample);
        else
            _trainingSamples.Add(sample);
    }
}
