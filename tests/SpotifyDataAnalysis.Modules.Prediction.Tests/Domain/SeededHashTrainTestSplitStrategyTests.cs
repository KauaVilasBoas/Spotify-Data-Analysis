using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// O split treino/teste é por hash estável da faixa + semente (DP-4a). Critério de aceite: <b>determinístico</b>
/// — a mesma faixa cai sempre do mesmo lado, olhando só para a faixa e a semente, independente de ordem,
/// quantidade e paralelismo de leitura.
/// </summary>
public sealed class SeededHashTrainTestSplitStrategyTests
{
    private static TrackTrainingSample Sample(string trackId)
        => TrackTrainingSample.FromEligibleCandidate(Candidates.Complete(trackId: trackId));

    private static SeededHashTrainTestSplitStrategy Strategy(int seed, double testFraction = 0.2)
        => new(TrainingDatasetSplitOptions.Create(seed, testFraction));

    [Fact]
    public void SameSeed_AssignsTheSamePartition_ForEveryKey()
    {
        SeededHashTrainTestSplitStrategy first = Strategy(seed: 20260730);
        SeededHashTrainTestSplitStrategy second = Strategy(seed: 20260730);

        for (int i = 0; i < 500; i++)
        {
            TrackTrainingSample sample = Sample($"track-{i}");
            Assert.Equal(first.AssignPartition(sample), second.AssignPartition(sample));
        }
    }

    [Fact]
    public void PartitionOfAKey_DoesNotDependOnReadOrder()
    {
        SeededHashTrainTestSplitStrategy strategy = Strategy(seed: 20260730);
        TrackTrainingSample sample = Sample("track-42");

        // Sortear outras faixas no meio não muda o veredito da faixa 42 — o hash olha só para a chave.
        DatasetPartition before = strategy.AssignPartition(sample);
        strategy.AssignPartition(Sample("track-1"));
        strategy.AssignPartition(Sample("track-2"));
        DatasetPartition after = strategy.AssignPartition(sample);

        Assert.Equal(before, after);
    }

    [Fact]
    public void DifferentSeed_ChangesTheAssignmentOfAtLeastSomeKeys()
    {
        SeededHashTrainTestSplitStrategy seedA = Strategy(seed: 1);
        SeededHashTrainTestSplitStrategy seedB = Strategy(seed: 2);

        int differences = 0;
        for (int i = 0; i < 500; i++)
        {
            TrackTrainingSample sample = Sample($"track-{i}");
            if (seedA.AssignPartition(sample) != seedB.AssignPartition(sample))
                differences++;
        }

        Assert.True(differences > 0, "sementes diferentes deveriam reparticionar ao menos algumas faixas");
    }

    [Fact]
    public void TestFraction_IsApproximatelyHonored_OverManyKeys()
    {
        SeededHashTrainTestSplitStrategy strategy = Strategy(seed: 20260730, testFraction: 0.3);

        const int total = 5_000;
        int test = 0;
        for (int i = 0; i < total; i++)
        {
            if (strategy.AssignPartition(Sample($"track-{i}")) == DatasetPartition.Test)
                test++;
        }

        double observed = (double)test / total;
        // Tolerância generosa: o hash é uniforme, não exato — o que importa é não estar longe da fração pedida.
        Assert.InRange(observed, 0.25, 0.35);
    }
}
