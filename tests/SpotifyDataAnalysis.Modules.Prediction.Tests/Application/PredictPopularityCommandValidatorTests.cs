using FluentValidation.Results;
using SpotifyDataAnalysis.Modules.Prediction.Application.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Inference;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Application;

/// <summary>
/// A validação de fronteira que garante 400 (e não 500 nem predição sem sentido): a invariante XOR da DP-1 e a
/// faixa de valores das features no modo "à mão".
/// </summary>
public sealed class PredictPopularityCommandValidatorTests
{
    private readonly PredictPopularityCommandValidator _validator = new();

    private static AudioFeaturesPayload ValidPayload() => new()
    {
        Danceability = 0.7,
        Energy = 0.65,
        Valence = 0.5,
        Tempo = 120.0,
        Acousticness = 0.1,
        Instrumentalness = 0.0,
        Liveness = 0.15,
        Speechiness = 0.05,
        Loudness = -6.2,
        DurationMs = 210_000,
        Explicit = false
    };

    private ValidationResult Validate(PopularityPredictionRequest request) =>
        _validator.Validate(new PredictPopularityCommand(request));

    [Fact]
    public void OnlyTrackId_IsValid()
    {
        Assert.True(Validate(new PopularityPredictionRequest { TrackId = "t1" }).IsValid);
    }

    [Fact]
    public void OnlyFeaturesInRange_IsValid()
    {
        Assert.True(Validate(new PopularityPredictionRequest { Features = ValidPayload() }).IsValid);
    }

    [Fact]
    public void BothModes_IsInvalid()
    {
        ValidationResult result = Validate(
            new PopularityPredictionRequest { TrackId = "t1", Features = ValidPayload() });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "mode");
    }

    [Fact]
    public void NeitherMode_IsInvalid()
    {
        ValidationResult result = Validate(new PopularityPredictionRequest());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "mode");
    }

    [Fact]
    public void FeatureOutOfUnitRange_IsInvalid()
    {
        var invalid = new AudioFeaturesPayload
        {
            Danceability = 1.5, Energy = 0.5, Valence = 0.5, Tempo = 120.0, Acousticness = 0.1,
            Instrumentalness = 0.0, Liveness = 0.2, Speechiness = 0.05, Loudness = -6.0,
            DurationMs = 200_000, Explicit = false
        };

        ValidationResult result = Validate(new PopularityPredictionRequest { Features = invalid });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "features.danceability");
    }

    [Fact]
    public void TempoOutOfRange_IsInvalid()
    {
        var invalid = new AudioFeaturesPayload
        {
            Danceability = 0.5, Energy = 0.5, Valence = 0.5, Tempo = 999.0, Acousticness = 0.1,
            Instrumentalness = 0.0, Liveness = 0.2, Speechiness = 0.05, Loudness = -6.0,
            DurationMs = 200_000, Explicit = false
        };

        ValidationResult result = Validate(new PopularityPredictionRequest { Features = invalid });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "features.tempo");
    }
}
