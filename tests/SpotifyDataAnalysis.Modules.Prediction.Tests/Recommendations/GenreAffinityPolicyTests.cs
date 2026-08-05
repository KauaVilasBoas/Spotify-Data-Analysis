using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// A Strategy de afinidade de gênero do E4.3 isolada: o boost aditivo por gênero compartilhado, o filtro duro, o
/// fallback gracioso quando a semente não tem gênero utilizável (ausente/imputado, DP-F) e a regra de que rótulo
/// imputado nunca boosta. Aritmética e regra puras — provável contra números à mão, sem índice nem banco.
/// </summary>
public sealed class GenreAffinityPolicyTests
{
    [Fact]
    public void Off_NeverBoostsNorFilters()
    {
        GenreAffinityPolicy policy = GenreAffinityPolicy.Create(
            GenreRankingMode.Off, seedGenre: "pop", seedGenreIsImputed: false);

        Assert.Equal(GenreRankingMode.Off, policy.Mode);
        Assert.False(policy.FellBackToCosineOnly);
        Assert.Null(policy.SeedGenre);
        Assert.True(policy.IsCandidateEligible("rock", candidateGenreIsImputed: false));
        Assert.Equal(0.0, policy.GenreBonusFor("pop", candidateGenreIsImputed: false));
    }

    [Fact]
    public void Boost_BonusOnlyForSameGenre()
    {
        GenreAffinityPolicy policy = GenreAffinityPolicy.Create(
            GenreRankingMode.Boost, seedGenre: "pop", seedGenreIsImputed: false, boostWeight: 0.2);

        Assert.Equal(0.2, policy.GenreBonusFor("pop", candidateGenreIsImputed: false));
        Assert.Equal(0.0, policy.GenreBonusFor("rock", candidateGenreIsImputed: false));
        Assert.Equal(0.0, policy.GenreBonusFor(candidateGenre: null, candidateGenreIsImputed: false));

        // Boost não exclui ninguém — só reordena.
        Assert.True(policy.IsCandidateEligible("rock", candidateGenreIsImputed: false));
    }

    [Fact]
    public void Boost_ImputedCandidateGenre_DoesNotEarnBonus()
    {
        // DP-F: um rótulo de gênero imputado é estimado, não medido — não boosta mesmo coincidindo.
        GenreAffinityPolicy policy = GenreAffinityPolicy.Create(
            GenreRankingMode.Boost, seedGenre: "pop", seedGenreIsImputed: false);

        Assert.Equal(0.0, policy.GenreBonusFor("pop", candidateGenreIsImputed: true));
    }

    [Fact]
    public void SameGenreOnly_FiltersOutOtherAndImputedGenres()
    {
        GenreAffinityPolicy policy = GenreAffinityPolicy.Create(
            GenreRankingMode.SameGenreOnly, seedGenre: "pop", seedGenreIsImputed: false);

        Assert.True(policy.IsCandidateEligible("pop", candidateGenreIsImputed: false));
        Assert.False(policy.IsCandidateEligible("rock", candidateGenreIsImputed: false));
        Assert.False(policy.IsCandidateEligible("pop", candidateGenreIsImputed: true));  // imputado não conta como mesmo gênero
        Assert.False(policy.IsCandidateEligible(candidateGenre: null, candidateGenreIsImputed: false));

        // O filtro já garante a coerência; não soma bônus por cima (não distorce a ordem por cosseno dentro do gênero).
        Assert.Equal(0.0, policy.GenreBonusFor("pop", candidateGenreIsImputed: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SeedWithoutGenre_FallsBackToOff_AndSignals(string? seedGenre)
    {
        GenreAffinityPolicy policy = GenreAffinityPolicy.Create(
            GenreRankingMode.Boost, seedGenre, seedGenreIsImputed: false);

        Assert.Equal(GenreRankingMode.Off, policy.Mode);
        Assert.True(policy.FellBackToCosineOnly);
        Assert.True(policy.IsCandidateEligible("pop", candidateGenreIsImputed: false));
        Assert.Equal(0.0, policy.GenreBonusFor("pop", candidateGenreIsImputed: false));
    }

    [Fact]
    public void SeedWithImputedGenre_FallsBackToOff_AndSignals()
    {
        // DP-F pelo lado da semente: gênero imputado não é base para boostar/filtrar.
        GenreAffinityPolicy policy = GenreAffinityPolicy.Create(
            GenreRankingMode.SameGenreOnly, seedGenre: "pop", seedGenreIsImputed: true);

        Assert.Equal(GenreRankingMode.Off, policy.Mode);
        Assert.True(policy.FellBackToCosineOnly);
        Assert.True(policy.IsCandidateEligible("rock", candidateGenreIsImputed: false));
    }

    [Fact]
    public void Create_NegativeBoostWeight_Throws()
    {
        Assert.Throws<DomainException>(() => GenreAffinityPolicy.Create(
            GenreRankingMode.Boost, seedGenre: "pop", seedGenreIsImputed: false, boostWeight: -0.1));
    }

    [Fact]
    public void CosineOnly_IsNeutral_WithoutFallbackFlag()
    {
        GenreAffinityPolicy policy = GenreAffinityPolicy.CosineOnly();

        Assert.Equal(GenreRankingMode.Off, policy.Mode);
        Assert.False(policy.FellBackToCosineOnly);  // off por ESCOLHA, não por fallback
    }
}
