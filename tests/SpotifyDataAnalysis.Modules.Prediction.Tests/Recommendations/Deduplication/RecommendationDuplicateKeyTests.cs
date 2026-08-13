using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Deduplication;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Deduplication;

/// <summary>
/// A chave "artista|título" do dedup (E4.7) — a reimplementação da regra do TrackMatchKey no Prediction (a
/// fronteira proíbe referenciar o VO do Catalog). Cobre o essencial que faz duas versões da mesma música colidirem:
/// sufixo editorial, parênteses, caixa, acento e pontuação.
/// </summary>
public sealed class RecommendationDuplicateKeyTests
{
    [Fact]
    public void SameSong_WithEditorialSuffix_ProducesTheSameKey()
    {
        var plain = RecommendationDuplicateKey.From("Everlong", "Foo Fighters");
        var remastered = RecommendationDuplicateKey.From("Everlong - Remastered 2011", "Foo Fighters");

        Assert.Equal(plain.Value, remastered.Value);
    }

    [Fact]
    public void SameSong_WithParentheticalAndCaseAndAccent_ProducesTheSameKey()
    {
        var a = RecommendationDuplicateKey.From("Déjà Vu (feat. Someone)", "Beyoncé");
        var b = RecommendationDuplicateKey.From("deja vu", "beyonce");

        Assert.Equal(a.Value, b.Value);
    }

    [Fact]
    public void DifferentArtists_SameTitle_ProduceDifferentKeys()
    {
        var a = RecommendationDuplicateKey.From("Yesterday", "The Beatles");
        var b = RecommendationDuplicateKey.From("Yesterday", "Boyz II Men");

        Assert.NotEqual(a.Value, b.Value);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void EmptyInputs_ProduceAnEmptyKey(string? title, string? artist)
    {
        Assert.True(RecommendationDuplicateKey.From(title, artist).IsEmpty);
    }

    [Fact]
    public void Apostrophes_AreDroppedNotSplit()
    {
        var withApostrophe = RecommendationDuplicateKey.From("Don't Stop", "Artist");
        var without = RecommendationDuplicateKey.From("Dont Stop", "Artist");

        Assert.Equal(withApostrophe.Value, without.Value);
    }
}
