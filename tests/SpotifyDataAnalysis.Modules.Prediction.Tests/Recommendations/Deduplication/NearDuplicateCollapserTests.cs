using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Deduplication;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Deduplication;

/// <summary>
/// O coração do dedup do top-N (E4.7): o colapso de quase-duplicatas por cosseno alto OU mesma chave "artista|
/// título", a escolha determinística do representante (DP-2) e a recomposição que ainda entrega <c>limit</c> itens
/// distintos. Aritmética pura no domínio — a integração com o índice/catálogo é coberta no handler.
/// </summary>
public sealed class NearDuplicateCollapserTests
{
    private static ExplainedTrackSimilarity Neighbor(string id, double cosine, bool imputed = false) =>
        new(id, cosine, cosine, GenreBonus: 0.0, SharesSeedGenre: false, IsImputed: imputed, Contributions: []);

    private static DeduplicationCandidate Candidate(
        string id, double cosine, string? name = null, string? artist = null,
        int? popularity = 50, bool imputed = false) =>
        new(Neighbor(id, cosine, imputed), RecommendationDuplicateKey.From(name, artist), popularity, imputed);

    /// <summary>Colapsador que declara duplicata só quando os ids estão no conjunto informado (cosine=1 para eles, 0 senão).</summary>
    private static NearDuplicateCollapser CollapserFor(params (string A, string B)[] duplicatePairs)
    {
        var pairs = new HashSet<(string, string)>();
        foreach ((string a, string b) in duplicatePairs)
        {
            pairs.Add((a, b));
            pairs.Add((b, a));
        }

        return new NearDuplicateCollapser((a, b) => pairs.Contains((a, b)) ? 1.0 : 0.0);
    }

    [Fact]
    public void Collapse_WithoutDuplicates_KeepsAllUpToLimit()
    {
        NearDuplicateCollapser collapser = CollapserFor();

        IReadOnlyList<CollapsedRecommendation> result = collapser.Collapse(
        [
            Candidate("a", 0.9, name: "A", artist: "X"),
            Candidate("b", 0.8, name: "B", artist: "Y"),
            Candidate("c", 0.7, name: "C", artist: "Z")
        ], limit: 3);

        Assert.Equal(new[] { "a", "b", "c" }, result.Select(r => r.Representative.TrackId));
        Assert.All(result, r => Assert.Equal(0, r.CollapsedDuplicateCount));
    }

    [Fact]
    public void Collapse_ByCosine_MergesTheSameSongWithDifferentIds()
    {
        // "a" e "dup" são a mesma música por cosseno alto; devem virar UM item, e "b" preenche a vaga liberada.
        NearDuplicateCollapser collapser = CollapserFor(("a", "dup"));

        IReadOnlyList<CollapsedRecommendation> result = collapser.Collapse(
        [
            Candidate("a", 0.99, name: "Song", artist: "Artist", popularity: 40),
            Candidate("dup", 0.98, name: "Song (Remaster)", artist: "Artist", popularity: 90),
            Candidate("b", 0.80, name: "Other", artist: "Other")
        ], limit: 2);

        Assert.Equal(2, result.Count);
        // Representante do grupo é o de maior popularity (dup=90), mas na posição da âncora (a).
        Assert.Equal("dup", result[0].Representative.TrackId);
        Assert.Equal(1, result[0].CollapsedDuplicateCount);
        Assert.Equal("b", result[1].Representative.TrackId);
        Assert.Equal(0, result[1].CollapsedDuplicateCount);
    }

    [Fact]
    public void Collapse_ByMatchKey_MergesEvenWhenCosineIsLow()
    {
        // Mesma chave "artista|título" (o sufixo editorial some na normalização); cosseno baixo (não são pares).
        NearDuplicateCollapser collapser = CollapserFor();

        IReadOnlyList<CollapsedRecommendation> result = collapser.Collapse(
        [
            Candidate("a", 0.90, name: "Everlong", artist: "Foo Fighters"),
            Candidate("dup", 0.10, name: "Everlong - Remastered", artist: "Foo Fighters")
        ], limit: 5);

        CollapsedRecommendation only = Assert.Single(result);
        Assert.Equal(1, only.CollapsedDuplicateCount);
    }

    [Fact]
    public void Collapse_PreservesRankingPosition_OfTheBestScoringVersion()
    {
        // Mesmo que o representante escolhido (DP-2) seja "dup", ele ocupa a posição da âncora (a de melhor score).
        NearDuplicateCollapser collapser = CollapserFor(("a", "dup"));

        IReadOnlyList<CollapsedRecommendation> result = collapser.Collapse(
        [
            Candidate("top", 0.999, name: "Top", artist: "T"),
            Candidate("a", 0.95, name: "Song", artist: "Artist", popularity: 10),
            Candidate("dup", 0.90, name: "Song", artist: "Artist", popularity: 99)
        ], limit: 3);

        Assert.Equal("top", result[0].Representative.TrackId);
        Assert.Equal("dup", result[1].Representative.TrackId); // representante do grupo, mas na 2ª posição (a âncora)
    }

    [Fact]
    public void Collapse_RepresentativeTieBreak_PrefersMeasuredOverImputed()
    {
        // Empate de popularity: a versão NÃO-imputada vence.
        NearDuplicateCollapser collapser = CollapserFor(("a", "dup"));

        IReadOnlyList<CollapsedRecommendation> result = collapser.Collapse(
        [
            Candidate("a", 0.99, name: "Song", artist: "Artist", popularity: 50, imputed: true),
            Candidate("dup", 0.98, name: "Song", artist: "Artist", popularity: 50, imputed: false)
        ], limit: 5);

        CollapsedRecommendation only = Assert.Single(result);
        Assert.Equal("dup", only.Representative.TrackId);
        Assert.False(only.Representative.IsImputed);
    }

    [Fact]
    public void Collapse_RepresentativeTieBreak_FallsBackToSmallestTrackId()
    {
        // Empate total (popularity e imputação): menor track_id ordinal — determinismo.
        NearDuplicateCollapser collapser = CollapserFor(("zzz", "aaa"));

        IReadOnlyList<CollapsedRecommendation> result = collapser.Collapse(
        [
            Candidate("zzz", 0.99, name: "Song", artist: "Artist", popularity: 50),
            Candidate("aaa", 0.98, name: "Song", artist: "Artist", popularity: 50)
        ], limit: 5);

        Assert.Equal("aaa", Assert.Single(result).Representative.TrackId);
    }

    [Fact]
    public void Collapse_DeliversLimitDistinctItems_WhenOverFetchedWithDuplicates()
    {
        // 5 candidatas, das quais 2 pares são a mesma música; o over-fetch garante 3 distintas para limit=3.
        NearDuplicateCollapser collapser = CollapserFor(("a", "a2"), ("b", "b2"));

        IReadOnlyList<CollapsedRecommendation> result = collapser.Collapse(
        [
            Candidate("a", 0.99, name: "A", artist: "X"),
            Candidate("a2", 0.98, name: "A", artist: "X"),
            Candidate("b", 0.95, name: "B", artist: "Y"),
            Candidate("b2", 0.94, name: "B", artist: "Y"),
            Candidate("c", 0.90, name: "C", artist: "Z")
        ], limit: 3);

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "a", "b", "c" }, result.Select(r => r.Representative.TrackId));
    }
}
