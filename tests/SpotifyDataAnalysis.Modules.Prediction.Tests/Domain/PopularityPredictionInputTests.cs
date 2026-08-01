using SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// A invariante XOR da DP-1 capturada no tipo: um insumo de predição é OU um <c>trackId</c> OU um bloco de
/// features, nunca os dois, nunca nenhum. É o que impede o handler de repetir essa checagem em cada caminho.
/// </summary>
public sealed class PopularityPredictionInputTests
{
    private static AudioFeatureInput SomeFeatures() =>
        AudioFeatureInput.Create(0.5, 0.6, 0.4, 120.0, 0.1, 0.0, 0.2, 0.05, -6.0, 200_000, false);

    [Fact]
    public void FromRequest_WithOnlyTrackId_ChoosesTrackIdMode()
    {
        PopularityPredictionInput input = PopularityPredictionInput.FromRequest("track-1", features: null);

        Assert.Equal(PopularityPredictionMode.ByTrackId, input.Mode);
        Assert.Equal("track-1", input.TrackId);
        Assert.Null(input.Features);
    }

    [Fact]
    public void FromRequest_WithOnlyFeatures_ChoosesFeaturesMode()
    {
        PopularityPredictionInput input = PopularityPredictionInput.FromRequest(trackId: null, SomeFeatures());

        Assert.Equal(PopularityPredictionMode.ByFeatures, input.Mode);
        Assert.NotNull(input.Features);
        Assert.Null(input.TrackId);
    }

    [Fact]
    public void FromRequest_WithBothModes_Throws()
    {
        Assert.Throws<DomainException>(
            () => PopularityPredictionInput.FromRequest("track-1", SomeFeatures()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromRequest_WithNeitherMode_Throws(string? trackId)
    {
        Assert.Throws<DomainException>(
            () => PopularityPredictionInput.FromRequest(trackId, features: null));
    }

    [Fact]
    public void FromRequest_TrimsTrackId()
    {
        PopularityPredictionInput input = PopularityPredictionInput.FromRequest("  track-1  ", features: null);

        Assert.Equal("track-1", input.TrackId);
    }
}
