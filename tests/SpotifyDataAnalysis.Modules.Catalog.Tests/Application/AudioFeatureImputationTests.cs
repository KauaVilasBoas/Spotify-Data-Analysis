using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Imputation;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Application;

/// <summary>
/// Testes do tratamento de faltantes (E1.5) no nível das peças: o cálculo das medianas por gênero e a
/// política que as aplica marcando a procedência.
/// </summary>
public sealed class AudioFeatureImputationTests
{
    private static KaggleAudioFeaturesRow Row(string? genre, double? energy = 0.5, int? key = 1)
        => new("id", "Song", "Artist", genre, DurationMs: 200_000,
            Danceability: 0.5, Energy: energy, Valence: 0.5, Tempo: 120, Acousticness: 0.1,
            Instrumentalness: 0.0, Liveness: 0.2, Speechiness: 0.05, Loudness: -5.0,
            Key: key, Mode: 1, TimeSignature: 4);

    private static AudioFeatureMedianProfile ProfileOf(params KaggleAudioFeaturesRow[] rows)
    {
        var builder = new AudioFeatureMedianProfileBuilder();
        foreach (KaggleAudioFeaturesRow row in rows)
            builder.Observe(row);

        return builder.Build();
    }

    [Fact]
    public void Median_OfAnOddSample_IsTheMiddleValue()
    {
        AudioFeatureMedianProfile profile = ProfileOf(
            Row("rock", energy: 0.1), Row("rock", energy: 0.7), Row("rock", energy: 0.3));

        Assert.Equal(0.3, profile.MedianFor(AudioFeature.Energy, "rock"));
    }

    [Fact]
    public void Median_OfAnEvenSample_IsTheAverageOfTheTwoMiddleValues()
    {
        AudioFeatureMedianProfile profile = ProfileOf(
            Row("rock", energy: 0.2), Row("rock", energy: 0.4));

        Assert.Equal(0.3, profile.MedianFor(AudioFeature.Energy, "rock")!.Value, precision: 10);
    }

    [Fact]
    public void Median_IsRobustToOutliers_UnlikeTheMean()
    {
        // A media seria ~25; a mediana continua 0.4.
        AudioFeatureMedianProfile profile = ProfileOf(
            Row("rock", energy: 0.3), Row("rock", energy: 0.4), Row("rock", energy: 0.5), Row("rock", energy: 100));

        Assert.Equal(0.45, profile.MedianFor(AudioFeature.Energy, "rock")!.Value, precision: 10);
    }

    [Fact]
    public void Median_IgnoresMissingValues_InsteadOfTreatingThemAsZero()
    {
        AudioFeatureMedianProfile profile = ProfileOf(
            Row("rock", energy: 0.8), Row("rock", energy: null), Row("rock", energy: 0.8));

        Assert.Equal(0.8, profile.MedianFor(AudioFeature.Energy, "rock"));
    }

    [Fact]
    public void MedianFor_FallsBackToGlobal_WhenTheGenreIsUnknown()
    {
        AudioFeatureMedianProfile profile = ProfileOf(Row("rock", energy: 0.8), Row("pop", energy: 0.2));

        Assert.Equal(0.5, profile.MedianFor(AudioFeature.Energy, "genero-inexistente")!.Value, precision: 10);
        Assert.Equal(0.5, profile.MedianFor(AudioFeature.Energy, genre: null)!.Value, precision: 10);
    }

    [Fact]
    public void MedianFor_IsCaseInsensitiveOnTheGenre()
    {
        AudioFeatureMedianProfile profile = ProfileOf(Row("Rock", energy: 0.8), Row("ROCK", energy: 0.8));

        Assert.Equal(0.8, profile.MedianFor(AudioFeature.Energy, "rock"));
    }

    [Fact]
    public void MedianFor_IsNull_WhenTheFeatureWasNeverObserved()
        => Assert.Null(AudioFeatureMedianProfile.Empty.MedianFor(AudioFeature.Energy, "rock"));

    [Fact]
    public void Builder_CountsTheRowsWithMissingValues()
    {
        var builder = new AudioFeatureMedianProfileBuilder();
        builder.Observe(Row("rock", energy: 0.5));
        builder.Observe(Row("rock", energy: null));
        builder.Observe(Row("rock", energy: null, key: null));

        Assert.Equal(2, builder.RowsWithMissingValues);
    }

    [Fact]
    public void Impute_LeavesACompleteRowUntouched_AndDoesNotFlagIt()
    {
        AudioFeatureMedianProfile profile = ProfileOf(Row("rock", energy: 0.9));

        ImputedAudioFeatures result = new MedianAudioFeatureImputer().Impute(Row("rock", energy: 0.25), profile);

        Assert.False(result.IsImputed);
        Assert.Empty(result.ImputedFeatures);
        Assert.Equal(0.25, result[AudioFeature.Energy]);
    }

    [Fact]
    public void Impute_ReportsExactlyWhichFeaturesWereInferred()
    {
        AudioFeatureMedianProfile profile = ProfileOf(Row("rock", energy: 0.6, key: 7));

        ImputedAudioFeatures result = new MedianAudioFeatureImputer()
            .Impute(Row("rock", energy: null, key: null), profile);

        Assert.True(result.IsImputed);
        Assert.Equal(new[] { AudioFeature.Key, AudioFeature.Energy }.Order(), result.ImputedFeatures.Order());
        Assert.Equal(0.6, result[AudioFeature.Energy]);
        Assert.Equal(7d, result[AudioFeature.Key]);
    }
}
