using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// A decomposição do cosseno por dimensão (E4.2, explicabilidade). O invariante que fecha o risco do card
/// ("explicação que não bate com o score"): a SOMA das contribuições por feature é exatamente o próprio cosseno.
/// Se este teste passa, "as features que mais aproximaram" nunca podem contradizer o ranking — são as maiores
/// parcelas de um mesmo número.
/// </summary>
public sealed class FeatureContributionTests
{
    private const double Tolerance = 1e-12;

    private static SimilarityFeatureVector Vector(params double[] coordinates) =>
        SimilarityFeatureVector.Create(coordinates);

    private static SimilarityFeatureVector Arbitrary(double seed) =>
        SimilarityFeatureVector.Create(
            Enumerable.Range(0, SimilarityFeatures.Dimension).Select(i => Math.Sin(seed + i) * (i + 1)).ToArray());

    [Fact]
    public void ContributionsBetween_SumEqualsCosine()
    {
        SimilarityFeatureVector left = Arbitrary(0.3);
        SimilarityFeatureVector right = Arbitrary(1.7);

        double cosine = CosineSimilarity.Between(left, right);
        double contributionSum = CosineSimilarity.ContributionsBetween(left, right).Sum();

        Assert.Equal(cosine, contributionSum, Tolerance);
    }

    [Fact]
    public void ContributionsBetween_HasOneEntryPerDimension()
    {
        IReadOnlyList<double> contributions =
            CosineSimilarity.ContributionsBetween(Arbitrary(0.1), Arbitrary(0.2));

        Assert.Equal(SimilarityFeatures.Dimension, contributions.Count);
    }

    [Fact]
    public void ContributionsBetween_ZeroMagnitudeVector_AllContributionsZero()
    {
        // Vetor na origem (norma zero): cosseno definido como 0, e a decomposição soma o mesmo — tudo zero.
        SimilarityFeatureVector origin = Vector(0, 0, 0, 0, 0, 0, 0, 0, 0);
        SimilarityFeatureVector other = Arbitrary(0.5);

        IReadOnlyList<double> contributions = CosineSimilarity.ContributionsBetween(origin, other);

        Assert.All(contributions, contribution => Assert.Equal(0.0, contribution));
        Assert.Equal(CosineSimilarity.Between(origin, other), contributions.Sum(), Tolerance);
    }

    [Fact]
    public void ContributionsBetween_IdenticalVectors_SumsToOne()
    {
        SimilarityFeatureVector vector = Arbitrary(0.9);

        double sum = CosineSimilarity.ContributionsBetween(vector, vector).Sum();

        Assert.Equal(1.0, sum, 1e-9);
    }
}
