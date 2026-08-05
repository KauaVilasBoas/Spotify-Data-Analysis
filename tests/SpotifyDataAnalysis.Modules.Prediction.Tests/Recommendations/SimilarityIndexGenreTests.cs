using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// O estágio de gênero do motor kNN (E4.3): o ranking híbrido (cosseno + boost de gênero) aplicado DENTRO da
/// varredura, o filtro duro por gênero, o resgate de faixas do mesmo gênero que perderiam no cosseno puro, e a
/// decomposição do score em cosseno + bônus na explicação. Índice real, catálogo pequeno, sem banco.
///
/// <para>Nota de calibração: num catálogo minúsculo os z-scores saturam e os cossenos colam em ±1 (variância baixa),
/// então os testes de RESGATE usam um peso de boost explícito que domina — provam o MECANISMO. Que o peso DEFAULT é
/// positivo e reordena a favor do mesmo gênero é coberto à parte; a calibração fina do default sobre o catálogo real
/// é do E4.4.</para>
/// </summary>
public sealed class SimilarityIndexGenreTests
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

    /// <summary>
    /// Semente "pop" com DOIS vizinhos "rock" de cosseno alto e DOIS "pop" de cosseno menor. No cosseno puro o
    /// top-2 é [rockA, rockB] (zero do gênero da semente); é o cenário em que o boost tem trabalho a fazer — trazer
    /// faixas do mesmo gênero de volta ao topo.
    /// </summary>
    private static IReadOnlyList<RawTrackFeatures> GenreCatalog() =>
    [
        new("seed",  Raw(0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0), Genre: "pop", false),
        new("rockA", Raw(0.79, 0.81, 0.79, 121.0, 0.11, 0.10, 0.10, 0.05, -8.1), Genre: "rock", false),
        new("rockB", Raw(0.81, 0.79, 0.81, 119.0, 0.09, 0.10, 0.10, 0.05, -7.9), Genre: "rock", false),
        new("popA",  Raw(0.68, 0.70, 0.69, 116.0, 0.20, 0.14, 0.14, 0.08, -10.0), Genre: "pop", false),
        new("popB",  Raw(0.66, 0.72, 0.67, 115.0, 0.22, 0.15, 0.13, 0.09, -10.5), Genre: "pop", false)
    ];

    private static GenreAffinityPolicy Boost(double weight) =>
        GenreAffinityPolicy.Create(GenreRankingMode.Boost, "pop", seedGenreIsImputed: false, weight);

    [Fact]
    public void Boost_LiftsSameGenreCoherenceOverCosineOnly_ForTheSameSeed()
    {
        // O critério de aceite central do E4.3: com gênero ligado o top-N tem MAIS faixas do gênero da semente que
        // com gênero desligado, para a MESMA semente. Peso explícito para dominar a saturação do micro-catálogo.
        SimilarityIndex index = SimilarityIndex.Build(GenreCatalog());

        IReadOnlyList<TrackSimilarity> cosineTop2 = index.FindNearestTo("seed", topN: 2)!;
        IReadOnlyList<TrackSimilarity> boostTop2 = index.FindNearestTo("seed", topN: 2, Boost(weight: 3.0))!;

        int cosineSameGenre = cosineTop2.Count(n => n.TrackId is "popA" or "popB");
        int boostSameGenre = boostTop2.Count(n => n.TrackId is "popA" or "popB");

        Assert.Equal(0, cosineSameGenre); // cosseno puro: top-2 são os dois rocks
        Assert.True(
            boostSameGenre > cosineSameGenre,
            $"O boost deveria elevar a presença do gênero da semente no top-N (boost={boostSameGenre}, off={cosineSameGenre}).");
    }

    [Fact]
    public void CosineOnly_PutsOtherGenreOnTop_BoostReordersInFavorOfSameGenre()
    {
        SimilarityIndex index = SimilarityIndex.Build(GenreCatalog());

        // Cosseno puro: o topo é de outro gênero (rock).
        Assert.Equal("rock", GenreOf(index, index.FindNearestTo("seed", topN: 1)![0].TrackId));
        // Com peso dominante, o topo passa a ser do mesmo gênero (pop).
        Assert.Equal("pop", GenreOf(index, index.FindNearestTo("seed", topN: 1, Boost(3.0))![0].TrackId));
    }

    [Fact]
    public void Boost_DefaultWeight_IsPositive_AndAddsToSameGenreScore()
    {
        // O default reordena a favor do mesmo gênero (bônus > 0 e score = cosseno + bônus), sem depender de o
        // micro-catálogo permitir o resgate — a magnitude que separa gêneros de fato é calibrada no E4.4.
        SimilarityIndex index = SimilarityIndex.Build(GenreCatalog());

        IReadOnlyList<TrackSimilarity> ranked =
            index.FindNearestTo("seed", topN: 10, GenreAffinityPolicy.Create(
                GenreRankingMode.Boost, "pop", seedGenreIsImputed: false))!;

        Assert.True(GenreAffinityPolicy.DefaultBoostWeight > 0);
        foreach (TrackSimilarity pop in ranked.Where(n => n.TrackId is "popA" or "popB"))
        {
            Assert.Equal(GenreAffinityPolicy.DefaultBoostWeight, pop.GenreBonus, Tolerance);
            Assert.Equal(pop.CosineSimilarity + pop.GenreBonus, pop.Similarity, Tolerance);
        }
        foreach (TrackSimilarity rock in ranked.Where(n => n.TrackId is "rockA" or "rockB"))
            Assert.Equal(0.0, rock.GenreBonus, Tolerance);
    }

    [Fact]
    public void SameGenreOnly_ReturnsOnlySameGenreNeighbors()
    {
        SimilarityIndex index = SimilarityIndex.Build(GenreCatalog());
        GenreAffinityPolicy filter = GenreAffinityPolicy.Create(
            GenreRankingMode.SameGenreOnly, "pop", seedGenreIsImputed: false);

        IReadOnlyList<TrackSimilarity> ranked = index.FindNearestTo("seed", topN: 10, filter)!;

        Assert.Equal(2, ranked.Count);
        Assert.All(ranked, n => Assert.Equal("pop", GenreOf(index, n.TrackId)));
        Assert.DoesNotContain(ranked, n => n.TrackId is "rockA" or "rockB");
    }

    [Fact]
    public void Explain_ReportsGenreBonusAndSharesFlag()
    {
        SimilarityIndex index = SimilarityIndex.Build(GenreCatalog());

        IReadOnlyList<ExplainedTrackSimilarity> explained =
            index.ExplainNearestTo("seed", topN: 10, Boost(0.2))!;

        ExplainedTrackSimilarity popA = explained.Single(n => n.TrackId == "popA");
        Assert.True(popA.SharesSeedGenre);
        Assert.Equal(0.2, popA.GenreBonus, Tolerance);
        // A soma das contribuições por feature reconstrói o COSSENO; o híbrido é cosseno + bônus.
        double contributionSum = popA.Contributions.Sum(c => c.Contribution);
        Assert.Equal(popA.CosineSimilarity, contributionSum, Tolerance);
        Assert.Equal(popA.CosineSimilarity + popA.GenreBonus, popA.Similarity, Tolerance);

        ExplainedTrackSimilarity rockA = explained.Single(n => n.TrackId == "rockA");
        Assert.False(rockA.SharesSeedGenre);
        Assert.Equal(0.0, rockA.GenreBonus, Tolerance);
    }

    [Fact]
    public void GenreOf_ReturnsIndexedGenre_OrNull()
    {
        SimilarityIndex index = SimilarityIndex.Build(GenreCatalog());

        Assert.Equal("pop", index.GenreOf("seed"));
        Assert.Equal("rock", index.GenreOf("rockA"));
        Assert.Null(index.GenreOf("unknown"));
    }

    private static string? GenreOf(SimilarityIndex index, string trackId) => index.GenreOf(trackId);
}
