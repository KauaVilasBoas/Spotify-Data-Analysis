using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Blending;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Blending;

/// <summary>
/// O coração do blend (E4.6): a soma ponderada de content normalizado (min-max) + Jaccard, a cobertura parcial
/// (união dos dois sinais, cada faixa marcada com sua origem) e a autoexclusão da semente. Aritmética pura — o
/// pipeline (índice, SQL, metadata) é coberto no handler.
/// </summary>
public sealed class RecommendationBlenderTests
{
    private static BlendContentCandidate Content(string id, double cosine) => new(id, cosine, Neighbor: null);
    private static BlendCollaborativeCandidate Collab(string id, double jaccard, int co = 5) => new(id, co, jaccard);

    [Fact]
    public void Blend_RejectsWeightOutOfRange()
    {
        Assert.Throws<DomainException>(() => new RecommendationBlender(-0.1));
        Assert.Throws<DomainException>(() => new RecommendationBlender(1.1));
    }

    [Fact]
    public void Blend_ContentOnlyTrack_HasContentSignal_AndZeroJaccard()
    {
        var blender = new RecommendationBlender(0.5);

        IReadOnlyList<BlendedRecommendation> result = blender.Blend(
            "seed",
            [Content("a", 0.9), Content("b", 0.5)],
            [],
            limit: 10);

        Assert.All(result, r => Assert.Equal(RecommendationSignal.ContentOnly, r.Signal));
        Assert.All(result, r => Assert.Equal(0.0, r.Jaccard));
    }

    [Fact]
    public void Blend_CollaborativeOnlyTrack_EntersWithoutAudio()
    {
        // "x" não está no content (pode nem ter vetor de áudio), mas co-ocorre — entra pelo colaborativo só.
        var blender = new RecommendationBlender(0.5);

        IReadOnlyList<BlendedRecommendation> result = blender.Blend(
            "seed",
            [Content("a", 0.9)],
            [Collab("x", 0.8)],
            limit: 10);

        BlendedRecommendation x = result.Single(r => r.TrackId == "x");
        Assert.Equal(RecommendationSignal.CollaborativeOnly, x.Signal);
        Assert.Equal(0.0, x.NormalizedContentScore); // sem áudio → parcela content zero
        Assert.Equal(0.8, x.Jaccard);
    }

    [Fact]
    public void Blend_TrackInBothSignals_IsBlended()
    {
        var blender = new RecommendationBlender(0.5);

        IReadOnlyList<BlendedRecommendation> result = blender.Blend(
            "seed",
            [Content("a", 0.9), Content("b", 0.5)],
            [Collab("a", 0.7)],
            limit: 10);

        BlendedRecommendation a = result.Single(r => r.TrackId == "a");
        Assert.Equal(RecommendationSignal.Blended, a.Signal);
        Assert.True(a.NormalizedContentScore > 0);
        Assert.Equal(0.7, a.Jaccard);
    }

    [Fact]
    public void Blend_WeightZero_IsPureContentOrder()
    {
        // w=0: o Jaccard não pesa; a ordem é a do content normalizado (a > b).
        var blender = new RecommendationBlender(0.0);

        IReadOnlyList<BlendedRecommendation> result = blender.Blend(
            "seed",
            [Content("a", 0.9), Content("b", 0.5)],
            [Collab("b", 1.0)], // b tem Jaccard máximo, mas w=0 o ignora
            limit: 10);

        Assert.Equal("a", result[0].TrackId);
    }

    [Fact]
    public void Blend_HighWeight_LetsCollaborativePromoteALowContentTrack()
    {
        // w alto: "b" (content fraco, Jaccard alto) ultrapassa "a" (content forte, sem co-ocorrência).
        var blender = new RecommendationBlender(0.9);

        IReadOnlyList<BlendedRecommendation> result = blender.Blend(
            "seed",
            [Content("a", 0.9), Content("b", 0.5)],
            [Collab("b", 1.0)],
            limit: 10);

        Assert.Equal("b", result[0].TrackId);
    }

    [Fact]
    public void Blend_MinMaxNormalizesContent_SoNarrowCosineRangeDoesNotDominate()
    {
        // Cossenos altos e estreitos (0,98 vs 0,97) reescalam para [1, 0]: sem min-max, ambos ~1 e o Jaccard nunca
        // desempataria. Com min-max, "b" (content pior) só ganha se o colaborativo o justificar.
        var blender = new RecommendationBlender(0.5);

        IReadOnlyList<BlendedRecommendation> result = blender.Blend(
            "seed",
            [Content("a", 0.98), Content("b", 0.97)],
            [Collab("b", 1.0)],
            limit: 10);

        BlendedRecommendation a = result.Single(r => r.TrackId == "a");
        BlendedRecommendation b = result.Single(r => r.TrackId == "b");
        Assert.Equal(1.0, a.NormalizedContentScore, 9); // melhor content → 1
        Assert.Equal(0.0, b.NormalizedContentScore, 9); // pior content → 0
        // final(a)=0,5·1+0,5·0=0,5 ; final(b)=0,5·0+0,5·1=0,5 — empate no score, desempate por Jaccard → b lidera.
        Assert.Equal("b", result[0].TrackId);
    }

    [Fact]
    public void Blend_ExcludesTheSeed_FromBothSignals()
    {
        var blender = new RecommendationBlender(0.5);

        IReadOnlyList<BlendedRecommendation> result = blender.Blend(
            "seed",
            [Content("seed", 1.0), Content("a", 0.8)],
            [Collab("seed", 1.0), Collab("a", 0.5)],
            limit: 10);

        Assert.DoesNotContain(result, r => r.TrackId == "seed");
    }

    [Fact]
    public void Blend_RespectsLimit()
    {
        var blender = new RecommendationBlender(0.5);

        IReadOnlyList<BlendedRecommendation> result = blender.Blend(
            "seed",
            [Content("a", 0.9), Content("b", 0.7), Content("c", 0.5)],
            [],
            limit: 2);

        Assert.Equal(2, result.Count);
    }
}
