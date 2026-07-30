using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// O assembler é o motor único por trás do endpoint de estatísticas e do treino do E3.2: aplica a
/// elegibilidade, conta cada exclusão pelo motivo e particiona os aprovados. Estes testes travam (1) o censo
/// que fecha a conta e (2) o determinismo integrado do split (mesma seed ⇒ os mesmos conjuntos).
/// </summary>
public sealed class TrainingDatasetAssemblerTests
{
    private static TrainingDatasetAssembler NewAssembler(
        int seed = 20260730,
        double testFraction = 0.2,
        ImputedFeaturePolicy policy = ImputedFeaturePolicy.ExcludeImputed)
    {
        TrainingDatasetSplitOptions options = TrainingDatasetSplitOptions.Create(seed, testFraction, policy);
        return new TrainingDatasetAssembler(
            options,
            new TrainingEligibilitySpecification(policy),
            new SeededHashTrainTestSplitStrategy(options));
    }

    [Fact]
    public void Census_CountsEachExclusionReason_AndTheCountsCloseTheAccount()
    {
        TrainingDatasetAssembler assembler = NewAssembler();

        // 3 medidas elegíveis, 1 sem popularidade, 1 sem features, 1 incompleta, 2 imputadas (excluídas pela política).
        assembler.Accept(Candidates.Complete("measured-1"));
        assembler.Accept(Candidates.Complete("measured-2"));
        assembler.Accept(Candidates.Complete("measured-3"));
        assembler.Accept(Candidates.Complete("no-pop") with { Popularity = null });
        assembler.Accept(Candidates.Complete("no-feat") with { HasAudioFeatures = false });
        assembler.Accept(Candidates.Complete("incomplete") with { Loudness = null });
        assembler.Accept(Candidates.Complete("imputed-1", isImputed: true));
        assembler.Accept(Candidates.Complete("imputed-2", isImputed: true));

        TrainingDatasetStatistics stats = assembler.Build().Statistics;

        Assert.Equal(8, stats.TotalTracks);
        Assert.Equal(1, stats.ExcludedMissingPopularity);
        Assert.Equal(1, stats.ExcludedMissingAudioFeatures);
        Assert.Equal(1, stats.ExcludedIncompleteAudioFeatures);
        Assert.Equal(3, stats.EligibleWithMeasuredFeatures);
        Assert.Equal(2, stats.EligibleWithImputedFeatures);
        Assert.Equal(2, stats.ExcludedByImputationPolicy);   // imputadas fora do treino (DP-2)
        Assert.Equal(3, stats.TrainableTracks);              // só as medidas foram particionadas

        // A conta fecha: total = exclusões estruturais + estruturalmente elegíveis.
        Assert.Equal(
            stats.ExcludedMissingPopularity
            + stats.ExcludedMissingAudioFeatures
            + stats.ExcludedIncompleteAudioFeatures
            + stats.StructurallyEligible,
            stats.TotalTracks);
        Assert.Equal(stats.EligibleWithMeasuredFeatures + stats.EligibleWithImputedFeatures, stats.StructurallyEligible);
    }

    [Fact]
    public void ImputedCandidate_IsCountedButNotPartitioned_UnderExcludePolicy()
    {
        TrainingDatasetAssembler assembler = NewAssembler(policy: ImputedFeaturePolicy.ExcludeImputed);

        assembler.Accept(Candidates.Complete("imputed", isImputed: true));

        TrainingDatasetPartitions partitions = assembler.Build();

        Assert.Empty(partitions.TrainingSamples);
        Assert.Empty(partitions.TestSamples);
        Assert.Equal(1, partitions.Statistics.EligibleWithImputedFeatures);
        Assert.Equal(1, partitions.Statistics.ExcludedByImputationPolicy);
    }

    [Fact]
    public void IncludeImputedPolicy_PartitionsTheImputedTracksToo()
    {
        TrainingDatasetAssembler assembler = NewAssembler(policy: ImputedFeaturePolicy.IncludeImputed);

        assembler.Accept(Candidates.Complete("measured", isImputed: false));
        assembler.Accept(Candidates.Complete("imputed", isImputed: true));

        TrainingDatasetStatistics stats = assembler.Build().Statistics;

        Assert.Equal(0, stats.ExcludedByImputationPolicy);
        Assert.Equal(2, stats.TrainableTracks);
    }

    [Fact]
    public void SameSeed_ProducesExactlyTheSameTrainAndTestSets_AcrossTwoRuns()
    {
        string[] ids = Enumerable.Range(0, 400).Select(i => $"track-{i}").ToArray();

        TrainingDatasetPartitions first = Run(ids);
        TrainingDatasetPartitions second = Run(ids);

        Assert.Equal(TrackIds(first.TrainingSamples), TrackIds(second.TrainingSamples));
        Assert.Equal(TrackIds(first.TestSamples), TrackIds(second.TestSamples));

        // E os conjuntos são disjuntos e cobrem todas as faixas elegíveis.
        Assert.Empty(TrackIds(first.TrainingSamples).Intersect(TrackIds(first.TestSamples)));
        Assert.Equal(ids.Length, first.TrainingSamples.Count + first.TestSamples.Count);
        Assert.NotEmpty(first.TestSamples); // o split reservou holdout

        static TrainingDatasetPartitions Run(string[] trackIds)
        {
            TrainingDatasetAssembler assembler = NewAssembler(seed: 12345);
            foreach (string id in trackIds)
                assembler.Accept(Candidates.Complete(id));
            return assembler.Build();
        }

        static IReadOnlyCollection<string> TrackIds(IReadOnlyList<TrackTrainingSample> samples)
            => samples.Select(sample => sample.TrackId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }
}
