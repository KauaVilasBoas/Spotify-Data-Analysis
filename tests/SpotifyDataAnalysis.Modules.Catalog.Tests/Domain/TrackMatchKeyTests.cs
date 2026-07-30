using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Domain;

/// <summary>
/// Testes da normalização de <see cref="TrackMatchKey"/> — a heurística que sustenta o fallback de
/// casamento com o dataset externo. Cada caso abaixo é uma divergência real entre o texto da API do Spotify
/// e o do dataset Kaggle.
/// </summary>
public sealed class TrackMatchKeyTests
{
    [Theory]
    // Caixa, espaços e pontuação não podem separar a mesma faixa.
    [InlineData("Bohemian Rhapsody", "Queen", "bohemian rhapsody", "queen")]
    [InlineData("  BOHEMIAN   RHAPSODY  ", "QUEEN", "Bohemian Rhapsody", "Queen")]
    [InlineData("Don't Stop Me Now", "Queen", "Dont Stop Me Now", "Queen")]
    // Acentuação: o dataset frequentemente vem sem.
    [InlineData("Coração", "Djavan", "Coracao", "Djavan")]
    // Sufixos editoriais que só o Spotify carrega.
    [InlineData("Bohemian Rhapsody - Remastered 2011", "Queen", "Bohemian Rhapsody", "Queen")]
    [InlineData("Song (feat. Someone)", "Artist", "Song", "Artist")]
    [InlineData("Song [Bonus Track]", "Artist", "Song", "Artist")]
    public void From_NormalizesCosmeticDifferences_ToTheSameKey(
        string leftTitle, string leftArtist, string rightTitle, string rightArtist)
        => Assert.Equal(TrackMatchKey.From(leftTitle, leftArtist), TrackMatchKey.From(rightTitle, rightArtist));

    [Fact]
    public void From_KeepsDifferentTracksApart()
        => Assert.NotEqual(
            TrackMatchKey.From("Bohemian Rhapsody", "Queen"),
            TrackMatchKey.From("Bohemian Rhapsody", "Panic! At The Disco"));

    [Fact]
    public void From_ProducesArtistPipeTitle()
        => Assert.Equal("queen|bohemian rhapsody", TrackMatchKey.From("Bohemian Rhapsody", "Queen").Value);

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", "  ")]
    [InlineData("()", "[]")]
    public void From_WithNothingUsable_IsEmpty(string? title, string? artist)
        => Assert.True(TrackMatchKey.From(title, artist).IsEmpty);

    [Fact]
    public void From_WithOnlyTheTitle_IsNotEmpty()
        => Assert.False(TrackMatchKey.From("Bohemian Rhapsody", null).IsEmpty);

    [Fact]
    public void FromArtistList_UsesOnlyThePrimaryArtist()
        => Assert.Equal(
            TrackMatchKey.From("Song", "Queen"),
            TrackMatchKey.FromArtistList("Song", "Queen;David Bowie;Outro"));

    [Fact]
    public void FromNormalized_RoundTripsThePersistedValue()
    {
        TrackMatchKey original = TrackMatchKey.From("Bohemian Rhapsody", "Queen");

        TrackMatchKey rehydrated = TrackMatchKey.FromNormalized(original.Value);

        Assert.Equal(original, rehydrated);
    }
}
