using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Domain;

/// <summary>
/// Testes do value object <see cref="Isrc"/> — normalização (hifens/caixa), validação estrutural do formato
/// ISO 3901 e a distinção entre a factory estrita (<see cref="Isrc.Of"/>) e a tolerante
/// (<see cref="Isrc.TryParse"/>), que é a usada pela ingestão.
/// </summary>
public sealed class IsrcTests
{
    [Theory]
    [InlineData("BRBMG0300729")]
    [InlineData("USUM71703861")]
    [InlineData("GBAYE0601498")]
    public void Of_WithWellFormedCode_KeepsTheValue(string code)
        => Assert.Equal(code, Isrc.Of(code).Value);

    [Theory]
    [InlineData("BR-BMG-03-00729", "BRBMG0300729")] // hifens são cosméticos
    [InlineData("brbmg0300729", "BRBMG0300729")]    // caixa é irrelevante
    [InlineData("  BRBMG0300729  ", "BRBMG0300729")]
    [InlineData("BR BMG 03 00729", "BRBMG0300729")]
    public void Of_NormalizesTheCode(string raw, string expected)
        => Assert.Equal(expected, Isrc.Of(raw).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BRBMG030072")]    // 11 caracteres
    [InlineData("BRBMG03007299")]  // 13 caracteres
    [InlineData("B1BMG0300729")]   // país precisa ser 2 letras
    [InlineData("BRBMG03007A9")]   // designação precisa ser numérica
    [InlineData("BRBM-0300729")]   // registrante alfanumérico faltando
    public void Of_WithMalformedCode_Throws(string? raw)
        => Assert.Throws<DomainException>(() => Isrc.Of(raw));

    [Fact]
    public void TryParse_WithWellFormedCode_Succeeds()
    {
        Assert.True(Isrc.TryParse("br-bmg-03-00729", out Isrc? isrc));
        Assert.Equal("BRBMG0300729", isrc!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-um-isrc")]
    public void TryParse_WithAbsentOrMalformedCode_FailsWithoutThrowing(string? raw)
    {
        Assert.False(Isrc.TryParse(raw, out Isrc? isrc));
        Assert.Null(isrc);
    }

    [Fact]
    public void Isrc_WithSameCodeInDifferentNotations_AreEqual()
        => Assert.Equal(Isrc.Of("BR-BMG-03-00729"), Isrc.Of("brbmg0300729"));

    [Fact]
    public void Isrc_WithDifferentCodes_AreNotEqual()
        => Assert.NotEqual(Isrc.Of("BRBMG0300729"), Isrc.Of("USUM71703861"));
}
