using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// As três métricas de regressão do projeto, sempre juntas. Separadas, cada uma engana: R² sozinho não diz o
/// erro em pontos de popularidade, e MAE sozinho não diz se o modelo explica algo além da média.
/// </summary>
/// <param name="RSquared">
/// Fração da variância explicada. Pode ser NEGATIVO — significa que o modelo erra mais que prever a média,
/// e é um resultado legítimo com alvo ruidoso, não um defeito de cálculo.
/// </param>
/// <param name="MeanAbsoluteError">Erro absoluto médio, na unidade do alvo (pontos de popularidade).</param>
/// <param name="RootMeanSquaredError">Raiz do erro quadrático médio; pune erro grande mais que o MAE.</param>
public sealed record RegressionMetrics(
    double RSquared,
    double MeanAbsoluteError,
    double RootMeanSquaredError);

/// <summary>
/// Cálculo das métricas a partir de pares (real, previsto). Vive no domínio e não na Infrastructure de
/// propósito: é aritmética pura, então dá para testá-la com números conhecidos, sem ML.NET e sem banco — e é
/// ela que produz as métricas do BASELINE, que nenhum framework calcula por nós.
/// </summary>
public static class RegressionMetricsCalculator
{
    /// <summary>
    /// Calcula R², MAE e RMSE. Usa as mesmas definições do <c>Regression.Evaluate</c> do ML.NET, para que as
    /// métricas do modelo e as do baseline sejam comparáveis — compará-las sob definições diferentes seria
    /// pior que não compará-las.
    /// </summary>
    /// <exception cref="DomainException">Quando não há observações, ou quando as sequências têm tamanhos diferentes.</exception>
    public static RegressionMetrics Calculate(
        IReadOnlyList<double> actuals,
        IReadOnlyList<double> predictions)
    {
        ArgumentNullException.ThrowIfNull(actuals);
        ArgumentNullException.ThrowIfNull(predictions);

        if (actuals.Count != predictions.Count)
            throw new DomainException(
                "Não dá para medir erro com séries de tamanhos diferentes: " +
                $"{actuals.Count} reais contra {predictions.Count} previstos.");

        if (actuals.Count == 0)
            throw new DomainException("Não dá para calcular métricas de regressão sobre um conjunto vazio.");

        double absoluteErrorSum = 0;
        double squaredErrorSum = 0;

        for (int index = 0; index < actuals.Count; index++)
        {
            double error = actuals[index] - predictions[index];
            absoluteErrorSum += Math.Abs(error);
            squaredErrorSum += error * error;
        }

        double meanAbsoluteError = absoluteErrorSum / actuals.Count;
        double rootMeanSquaredError = Math.Sqrt(squaredErrorSum / actuals.Count);

        return new RegressionMetrics(
            RSquaredOf(actuals, squaredErrorSum),
            meanAbsoluteError,
            rootMeanSquaredError);
    }

    /// <summary>Previsão constante para todo o conjunto — a forma do baseline "prever sempre a média".</summary>
    public static RegressionMetrics CalculateForConstantPrediction(
        IReadOnlyList<double> actuals,
        double constantPrediction)
    {
        ArgumentNullException.ThrowIfNull(actuals);

        double[] predictions = new double[actuals.Count];
        Array.Fill(predictions, constantPrediction);

        return Calculate(actuals, predictions);
    }

    /// <summary>
    /// R² = 1 − SS_res / SS_tot, com SS_tot medido contra a média do PRÓPRIO conjunto avaliado. Quando o alvo
    /// não varia (SS_tot = 0), a razão é indefinida: devolvemos 0, que é o mesmo que dizer "não há variância
    /// para explicar" — inventar 1 aqui daria a um modelo qualquer um R² perfeito sobre um conjunto constante.
    /// </summary>
    private static double RSquaredOf(IReadOnlyList<double> actuals, double squaredErrorSum)
    {
        double mean = actuals.Average();
        double totalSumOfSquares = actuals.Sum(actual => (actual - mean) * (actual - mean));

        return totalSumOfSquares == 0 ? 0 : 1 - (squaredErrorSum / totalSumOfSquares);
    }
}
