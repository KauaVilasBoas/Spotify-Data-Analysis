using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// A explicabilidade do motor (E4.2): <see cref="SimilarityIndex.ExplainNearestTo"/> devolve o MESMO ranking de
/// <see cref="SimilarityIndex.FindNearestTo(string,int)"/>, enriquecido com a decomposição do cosseno por
/// dimensão. Prova que a explicação (a) não muda o ranking, (b) soma exatamente o score de cada vizinha, e (c)
/// reporta os valores ORIGINAIS (crus), não os normalizados.
/// </summary>
public sealed class SimilarityIndexExplainTests
{
    private const double Tolerance = 1e-9;

    private static SimilarityFeatureVector Raw(
        double danceability, double energy, double valence, double tempo,
        double acousticness, double instrumentalness, double liveness, double speechiness, double loudness) =>
        SimilarityFeatureVector.Create(
        [
            danceability, energy, valence, tempo, acousticness,
            instrumentalness, liveness, speechiness, loudness
        ]);

    private static readonly GenreAffinityPolicy CosineOnly = GenreAffinityPolicy.CosineOnly();

    private static IReadOnlyList<RawTrackFeatures> SampleCatalog() =>
    [
        new("seed", Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), Genre: null, false),
        new("near", Raw(0.79, 0.81, 0.78, 122.0, 0.11, 0.10, 0.10, 0.05, -8.2), Genre: null, false),
        new("mid",  Raw(0.55, 0.55, 0.55, 110.0, 0.40, 0.20, 0.15, 0.06, -12.0), Genre: null, false),
        new("far",  Raw(0.10, 0.10, 0.10, 60.0,  0.90, 0.80, 0.70, 0.60, -30.0), Genre: null, false),
        new("imp",  Raw(0.78, 0.82, 0.79, 121.0, 0.12, 0.11, 0.10, 0.05, -8.1), Genre: null, IsImputed: true)
    ];

    [Fact]
    public void ExplainNearestTo_UnknownSeed_ReturnsNull()
    {
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        Assert.Null(index.ExplainNearestTo("unknown", topN: 3, CosineOnly));
    }

    [Fact]
    public void ExplainNearestTo_MatchesTheRankingOfFindNearestTo()
    {
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        IReadOnlyList<TrackSimilarity> ranked = index.FindNearestTo("seed", topN: 10)!;
        IReadOnlyList<ExplainedTrackSimilarity> explained = index.ExplainNearestTo("seed", topN: 10, CosineOnly)!;

        Assert.Equal(ranked.Count, explained.Count);
        for (int i = 0; i < ranked.Count; i++)
        {
            Assert.Equal(ranked[i].TrackId, explained[i].TrackId);
            Assert.Equal(ranked[i].Similarity, explained[i].Similarity, Tolerance);
            Assert.Equal(ranked[i].IsImputed, explained[i].IsImputed);
        }
    }

    [Fact]
    public void ExplainNearestTo_ContributionsSumToTheCosine_WhenGenreDoesNotWeigh()
    {
        // O invariante que sustenta a explicabilidade: a soma das contribuições por feature É o cosseno da vizinha.
        // Sem gênero, o score híbrido coincide com o cosseno, então a soma também reconstrói o Similarity.
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        IReadOnlyList<ExplainedTrackSimilarity> explained = index.ExplainNearestTo("seed", topN: 10, CosineOnly)!;

        foreach (ExplainedTrackSimilarity neighbor in explained)
        {
            double contributionSum = neighbor.Contributions.Sum(contribution => contribution.Contribution);
            Assert.Equal(neighbor.CosineSimilarity, contributionSum, Tolerance);
            Assert.Equal(0.0, neighbor.GenreBonus, Tolerance);
            Assert.Equal(neighbor.Similarity, contributionSum, Tolerance);
        }
    }

    [Fact]
    public void ExplainNearestTo_HasOneContributionPerFeature_InCanonicalOrder()
    {
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        ExplainedTrackSimilarity neighbor = index.ExplainNearestTo("seed", topN: 1, CosineOnly)![0];

        Assert.Equal(SimilarityFeatures.Dimension, neighbor.Contributions.Count);
        for (int i = 0; i < SimilarityFeatures.Ordered.Count; i++)
            Assert.Equal(SimilarityFeatures.Ordered[i], neighbor.Contributions[i].Feature);
    }

    [Fact]
    public void ExplainNearestTo_ReportsOriginalFeatureValues_NotNormalized()
    {
        // "near": energy 0.81 (candidata) contra 0.80 da semente. Os valores reportados devem ser esses CRUS,
        // não os z-scores — senão o número não diz nada ao usuário (risco do card).
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        ExplainedTrackSimilarity near =
            index.ExplainNearestTo("seed", topN: 10, CosineOnly)!.Single(n => n.TrackId == "near");

        FeatureContribution energy = near.Contributions.Single(c => c.Feature == SimilarityFeature.Energy);
        Assert.Equal(0.80, energy.SeedValue, Tolerance);
        Assert.Equal(0.81, energy.CandidateValue, Tolerance);

        FeatureContribution tempo = near.Contributions.Single(c => c.Feature == SimilarityFeature.Tempo);
        Assert.Equal(120.0, tempo.SeedValue, Tolerance);
        Assert.Equal(122.0, tempo.CandidateValue, Tolerance);
    }

    [Fact]
    public void ExplainNearestTo_CarriesImputedFlag()
    {
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        ExplainedTrackSimilarity imputed =
            index.ExplainNearestTo("seed", topN: 10, CosineOnly)!.Single(n => n.TrackId == "imp");

        Assert.True(imputed.IsImputed);
    }
}
