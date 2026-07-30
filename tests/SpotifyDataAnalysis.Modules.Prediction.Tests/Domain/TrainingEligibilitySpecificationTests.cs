using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// A regra de elegibilidade é o coração do E3.1: define quem entra no dataset de treino. Estes testes travam o
/// critério de aceite central — <b>faixa sem popularidade ou sem audio-features NUNCA entra</b> — e a ordem em
/// cascata que garante que o primeiro motivo encontrado é o motivo relatado.
/// </summary>
public sealed class TrainingEligibilitySpecificationTests
{
    private static readonly TrainingEligibilitySpecification ExcludeImputed =
        new(ImputedFeaturePolicy.ExcludeImputed);

    private static readonly TrainingEligibilitySpecification IncludeImputed =
        new(ImputedFeaturePolicy.IncludeImputed);

    [Fact]
    public void MissingPopularity_IsExcluded_AndNeverEligible()
    {
        TrackTrainingCandidate candidate = Candidates.Complete() with { Popularity = null };

        TrainingEligibilityVerdict verdict = ExcludeImputed.Evaluate(candidate);

        Assert.False(verdict.IsEligible);
        Assert.Equal(TrainingExclusionReason.MissingPopularity, verdict.ExclusionReason);
    }

    [Fact]
    public void MissingAudioFeatures_IsExcluded_AndNeverEligible()
    {
        TrackTrainingCandidate candidate = Candidates.Complete() with { HasAudioFeatures = false };

        TrainingEligibilityVerdict verdict = ExcludeImputed.Evaluate(candidate);

        Assert.False(verdict.IsEligible);
        Assert.Equal(TrainingExclusionReason.MissingAudioFeatures, verdict.ExclusionReason);
    }

    [Fact]
    public void IncompleteAudioFeatures_IsExcluded_WhenAnyNumericGrandezaIsMissing()
    {
        // Falta uma única grandeza (Energy) no jsonb — a faixa não é treinável.
        TrackTrainingCandidate candidate = Candidates.Complete() with { Energy = null };

        TrainingEligibilityVerdict verdict = ExcludeImputed.Evaluate(candidate);

        Assert.False(verdict.IsEligible);
        Assert.Equal(TrainingExclusionReason.IncompleteAudioFeatures, verdict.ExclusionReason);
    }

    [Fact]
    public void ImputedFeatures_AreExcluded_UnderTheDefaultExcludePolicy()
    {
        TrackTrainingCandidate candidate = Candidates.Complete(isImputed: true);

        TrainingEligibilityVerdict verdict = ExcludeImputed.Evaluate(candidate);

        Assert.False(verdict.IsEligible);
        Assert.Equal(TrainingExclusionReason.ImputedAudioFeatures, verdict.ExclusionReason);
    }

    [Fact]
    public void ImputedFeatures_AreEligible_WhenThePolicyIncludesThem()
    {
        TrackTrainingCandidate candidate = Candidates.Complete(isImputed: true);

        TrainingEligibilityVerdict verdict = IncludeImputed.Evaluate(candidate);

        Assert.True(verdict.IsEligible);
        Assert.Null(verdict.ExclusionReason);
    }

    [Fact]
    public void CompleteMeasuredTrack_IsEligible()
    {
        TrainingEligibilityVerdict verdict = ExcludeImputed.Evaluate(Candidates.Complete());

        Assert.True(verdict.IsEligible);
        Assert.Null(verdict.ExclusionReason);
    }

    [Fact]
    public void Cascade_ReportsTheFirstReason_PopularityBeforeFeatures()
    {
        // Sem popularidade E sem features: o primeiro motivo da cascata (popularidade) é o relatado.
        TrackTrainingCandidate candidate =
            Candidates.Complete() with { Popularity = null, HasAudioFeatures = false };

        TrainingEligibilityVerdict verdict = ExcludeImputed.Evaluate(candidate);

        Assert.Equal(TrainingExclusionReason.MissingPopularity, verdict.ExclusionReason);
    }
}
