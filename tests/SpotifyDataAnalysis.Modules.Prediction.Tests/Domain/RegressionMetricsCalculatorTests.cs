using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// As métricas são aritmética pura, então dá para prová-las contra números calculados à mão — o que nenhum
/// teste sobre o resultado do ML.NET conseguiria fazer. É este teste que garante que o BASELINE, calculado por
/// nós, é comparável ao número que o framework produz para o modelo.
/// </summary>
public sealed class RegressionMetricsCalculatorTests
{
    [Fact]
    public void Calculate_WithPerfectPredictions_HasNoErrorAndExplainsEverything()
    {
        double[] actuals = [10, 20, 30, 40];

        RegressionMetrics metrics = RegressionMetricsCalculator.Calculate(actuals, actuals);

        Assert.Equal(0, metrics.MeanAbsoluteError, precision: 10);
        Assert.Equal(0, metrics.RootMeanSquaredError, precision: 10);
        Assert.Equal(1, metrics.RSquared, precision: 10);
    }

    [Fact]
    public void Calculate_ComputesMaeAndRmseFromKnownErrors()
    {
        double[] actuals = [10, 20, 30];
        double[] predictions = [12, 18, 33];

        // Erros: -2, +2, -3 → MAE = 7/3 ; RMSE = sqrt((4+4+9)/3) = sqrt(17/3)
        RegressionMetrics metrics = RegressionMetricsCalculator.Calculate(actuals, predictions);

        Assert.Equal(7d / 3d, metrics.MeanAbsoluteError, precision: 10);
        Assert.Equal(Math.Sqrt(17d / 3d), metrics.RootMeanSquaredError, precision: 10);
    }

    [Fact]
    public void Calculate_PredictingTheMean_GivesRSquaredZero()
    {
        // Prever a média do próprio conjunto é, por definição, R² = 0: é o piso que o modelo tem de bater.
        double[] actuals = [10, 20, 30, 40];

        RegressionMetrics metrics =
            RegressionMetricsCalculator.CalculateForConstantPrediction(actuals, actuals.Average());

        Assert.Equal(0, metrics.RSquared, precision: 10);
    }

    [Fact]
    public void Calculate_WorseThanTheMean_GivesNegativeRSquared()
    {
        // R² negativo é resultado legítimo, não erro de cálculo — e com alvo ruidoso ele acontece.
        double[] actuals = [10, 20, 30, 40];
        double[] predictions = [100, 100, 100, 100];

        RegressionMetrics metrics = RegressionMetricsCalculator.Calculate(actuals, predictions);

        Assert.True(metrics.RSquared < 0);
    }

    [Fact]
    public void Calculate_WithConstantTarget_ReportsZeroRSquaredInsteadOfInventingAPerfectScore()
    {
        // Sem variância no alvo não há nada a explicar; devolver 1 daria nota máxima a qualquer modelo.
        double[] actuals = [50, 50, 50];
        double[] predictions = [50, 50, 50];

        RegressionMetrics metrics = RegressionMetricsCalculator.Calculate(actuals, predictions);

        Assert.Equal(0, metrics.RSquared, precision: 10);
    }

    [Fact]
    public void Calculate_WithMismatchedSeries_Fails()
    {
        Assert.Throws<DomainException>(
            () => RegressionMetricsCalculator.Calculate([1, 2, 3], [1, 2]));
    }

    [Fact]
    public void Calculate_WithNoObservations_Fails()
    {
        Assert.Throws<DomainException>(() => RegressionMetricsCalculator.Calculate([], []));
    }
}
