using SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// A validação de faixa das audio-features na criação do value object: um valor fora do domínio do Spotify é
/// rejeitado no domínio, e não vira uma predição plausível e sem sentido (E3.5).
/// </summary>
public sealed class AudioFeatureInputTests
{
    private static AudioFeatureInput CreateValid(
        double danceability = 0.5,
        double tempo = 120.0,
        double loudness = -6.0,
        int durationMs = 200_000) =>
        AudioFeatureInput.Create(
            danceability: danceability,
            energy: 0.6,
            valence: 0.4,
            tempo: tempo,
            acousticness: 0.1,
            instrumentalness: 0.0,
            liveness: 0.2,
            speechiness: 0.05,
            loudness: loudness,
            durationMs: durationMs,
            @explicit: false);

    [Fact]
    public void Create_WithFeaturesInRange_Succeeds()
    {
        AudioFeatureInput features = CreateValid();

        Assert.Equal(0.5, features.Danceability);
        Assert.Equal(120.0, features.Tempo);
        Assert.Equal(-6.0, features.Loudness);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Create_WithUnitFeatureOutOfRange_Throws(double danceability)
    {
        Assert.Throws<DomainException>(() => CreateValid(danceability: danceability));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Create_WithUnitFeatureAtBoundary_Succeeds(double danceability)
    {
        AudioFeatureInput features = CreateValid(danceability: danceability);

        Assert.Equal(danceability, features.Danceability);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(301.0)]
    public void Create_WithTempoOutOfRange_Throws(double tempo)
    {
        Assert.Throws<DomainException>(() => CreateValid(tempo: tempo));
    }

    [Theory]
    [InlineData(-61.0)]
    [InlineData(6.0)]
    public void Create_WithLoudnessOutOfRange_Throws(double loudness)
    {
        Assert.Throws<DomainException>(() => CreateValid(loudness: loudness));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(7_200_001)]
    public void Create_WithDurationOutOfRange_Throws(int durationMs)
    {
        Assert.Throws<DomainException>(() => CreateValid(durationMs: durationMs));
    }

    [Fact]
    public void Equality_IsStructural()
    {
        Assert.Equal(CreateValid(), CreateValid());
        Assert.NotEqual(CreateValid(danceability: 0.5), CreateValid(danceability: 0.6));
    }
}
