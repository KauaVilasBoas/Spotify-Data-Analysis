using SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// O clamp da saída em [0, 100] (E3.5): a regressão pode extrapolar, e devolver 103 destrói a credibilidade da
/// demo. O value object limita, mas preserva o score cru e sinaliza quando houve extrapolação — honestidade, não
/// maquiagem.
/// </summary>
public sealed class PredictedPopularityTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(50.0)]
    [InlineData(100.0)]
    public void FromRawScore_WithinDomain_KeepsValue_AndDoesNotFlagClamp(double raw)
    {
        PredictedPopularity prediction = PredictedPopularity.FromRawScore(raw);

        Assert.Equal(raw, prediction.Value);
        Assert.Equal(raw, prediction.RawScore);
        Assert.False(prediction.WasClamped);
    }

    [Fact]
    public void FromRawScore_AboveMaximum_ClampsToHundred_AndFlags()
    {
        PredictedPopularity prediction = PredictedPopularity.FromRawScore(103.4);

        Assert.Equal(100.0, prediction.Value);
        Assert.Equal(103.4, prediction.RawScore);
        Assert.True(prediction.WasClamped);
    }

    [Fact]
    public void FromRawScore_BelowMinimum_ClampsToZero_AndFlags()
    {
        PredictedPopularity prediction = PredictedPopularity.FromRawScore(-4.2);

        Assert.Equal(0.0, prediction.Value);
        Assert.Equal(-4.2, prediction.RawScore);
        Assert.True(prediction.WasClamped);
    }

    [Fact]
    public void FromRawScore_NaN_ClampsToZero_AndFlags()
    {
        PredictedPopularity prediction = PredictedPopularity.FromRawScore(double.NaN);

        Assert.Equal(0.0, prediction.Value);
        Assert.True(prediction.WasClamped);
    }
}
