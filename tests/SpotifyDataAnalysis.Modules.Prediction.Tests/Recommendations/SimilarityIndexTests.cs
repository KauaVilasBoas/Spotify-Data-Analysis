using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// O motor kNN: montagem do índice (aprende μ/σ e normaliza), autoexclusão da semente, ordenação decrescente,
/// marcação de imputadas (DP-F) e consistência semente/candidato ponta a ponta.
/// </summary>
public sealed class SimilarityIndexTests
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

    private static RawTrackFeatures Track(
        string id, SimilarityFeatureVector raw, string? genre = null, bool imputed = false) =>
        new(id, raw, genre, imputed);

    /// <summary>Um catálogo pequeno com uma semente e vizinhos de proximidade conhecida (ordenável à mão).</summary>
    private static IReadOnlyList<RawTrackFeatures> SampleCatalog() =>
    [
        Track("seed",  Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0)),
        Track("near",  Raw(0.79, 0.81, 0.78, 122.0, 0.11, 0.10, 0.10, 0.05, -8.2)),
        Track("mid",   Raw(0.55, 0.55, 0.55, 110.0, 0.40, 0.20, 0.15, 0.06, -12.0)),
        Track("far",   Raw(0.10, 0.10, 0.10, 60.0,  0.90, 0.80, 0.70, 0.60, -30.0)),
        Track("imp",   Raw(0.78, 0.82, 0.79, 121.0, 0.12, 0.11, 0.10, 0.05, -8.1), imputed: true)
    ];

    [Fact]
    public void Build_EmptyCatalog_Throws()
    {
        Assert.Throws<DomainException>(() => SimilarityIndex.Build([]));
    }

    [Fact]
    public void Build_IndexesEveryTrack()
    {
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        Assert.Equal(5, index.Count);
        Assert.True(index.ContainsTrack("seed"));
        Assert.False(index.ContainsTrack("unknown"));
    }

    [Fact]
    public void FindNearestTo_UnknownSeed_ReturnsNull()
    {
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        Assert.Null(index.FindNearestTo("unknown", topN: 3));
    }

    [Fact]
    public void FindNearestTo_ExcludesTheSeedItself()
    {
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        IReadOnlyList<TrackSimilarity> neighbors = index.FindNearestTo("seed", topN: 10)!;

        Assert.DoesNotContain(neighbors, neighbor => neighbor.TrackId == "seed");
        Assert.Equal(4, neighbors.Count); // 5 no índice menos a própria semente
    }

    [Fact]
    public void FindNearestTo_OrdersByDescendingSimilarity()
    {
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        IReadOnlyList<TrackSimilarity> neighbors = index.FindNearestTo("seed", topN: 10)!;

        for (int i = 1; i < neighbors.Count; i++)
            Assert.True(
                neighbors[i - 1].Similarity >= neighbors[i].Similarity,
                "Os vizinhos devem vir em ordem decrescente de similaridade.");
    }

    [Fact]
    public void FindNearestTo_ReturnsTheActuallyClosestNeighborFirst()
    {
        // "near"/"imp" são quase a semente → devem vir na frente de "mid"/"far".
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        IReadOnlyList<TrackSimilarity> neighbors = index.FindNearestTo("seed", topN: 2)!;

        string[] topIds = neighbors.Select(n => n.TrackId).ToArray();
        Assert.Contains("near", topIds);
        Assert.DoesNotContain("far", topIds);
    }

    [Fact]
    public void FindNearestTo_RespectsTopN()
    {
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        IReadOnlyList<TrackSimilarity> neighbors = index.FindNearestTo("seed", topN: 2)!;

        Assert.Equal(2, neighbors.Count);
    }

    [Fact]
    public void FindNearestTo_CarriesImputedFlagOnNeighbors()
    {
        // DP-F: a vizinha imputada aparece marcada; imputado nunca passa por medido em silêncio.
        SimilarityIndex index = SimilarityIndex.Build(SampleCatalog());

        IReadOnlyList<TrackSimilarity> neighbors = index.FindNearestTo("seed", topN: 10)!;

        TrackSimilarity imputedNeighbor = neighbors.Single(n => n.TrackId == "imp");
        Assert.True(imputedNeighbor.IsImputed);
        Assert.All(neighbors.Where(n => n.TrackId != "imp"), n => Assert.False(n.IsImputed));
    }

    [Fact]
    public void FindNearestTo_SeedIsTheNearestNeighborOfItself_WhenAllowedToAppear()
    {
        // Sanidade do card: a faixa é o vizinho mais próximo de si mesma. Provamos usando a MESMA faixa como
        // semente externa (vetor cru) SEM autoexcluir por id — ela deve casar consigo com similaridade ~1 e
        // liderar o ranking.
        IReadOnlyList<RawTrackFeatures> catalog = SampleCatalog();
        SimilarityIndex index = SimilarityIndex.Build(catalog);

        SimilarityFeatureVector seedRaw = catalog.Single(t => t.TrackId == "seed").RawVector;
        IReadOnlyList<TrackSimilarity> neighbors = index.FindNearestTo(seedRaw, topN: 5);

        TrackSimilarity best = neighbors[0];
        Assert.Equal("seed", best.TrackId);
        Assert.Equal(1.0, best.Similarity, 6);
    }

    [Fact]
    public void FindNearestTo_ExternalSeedVector_UsesSameNormalizationAsCandidates()
    {
        // Consistência semente/candidato ponta a ponta: buscar por "seed" via id, ou pelo vetor CRU externo de
        // "seed" (autoexcluindo o id), tem de dar EXATAMENTE o mesmo ranking — a normalização é a mesma dos dois
        // lados. Se a semente externa passasse por outra transformação, os scores divergiriam.
        IReadOnlyList<RawTrackFeatures> catalog = SampleCatalog();
        SimilarityIndex index = SimilarityIndex.Build(catalog);
        SimilarityFeatureVector seedRaw = catalog.Single(t => t.TrackId == "seed").RawVector;

        IReadOnlyList<TrackSimilarity> byId = index.FindNearestTo("seed", topN: 10)!;
        IReadOnlyList<TrackSimilarity> byVector = index.FindNearestTo(seedRaw, topN: 10, excludeTrackId: "seed");

        Assert.Equal(byId.Count, byVector.Count);
        for (int i = 0; i < byId.Count; i++)
        {
            Assert.Equal(byId[i].TrackId, byVector[i].TrackId);
            Assert.Equal(byId[i].Similarity, byVector[i].Similarity, Tolerance);
        }
    }

    [Fact]
    public void FindNearestTo_ExternalSeed_HonorsExcludeId()
    {
        IReadOnlyList<RawTrackFeatures> catalog = SampleCatalog();
        SimilarityIndex index = SimilarityIndex.Build(catalog);
        SimilarityFeatureVector seedRaw = catalog.Single(t => t.TrackId == "seed").RawVector;

        IReadOnlyList<TrackSimilarity> neighbors = index.FindNearestTo(seedRaw, topN: 10, excludeTrackId: "seed");

        Assert.DoesNotContain(neighbors, n => n.TrackId == "seed");
    }
}
