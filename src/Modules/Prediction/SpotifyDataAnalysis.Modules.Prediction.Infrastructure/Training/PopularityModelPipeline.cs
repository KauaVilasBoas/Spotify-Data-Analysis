using Microsoft.ML;
using Microsoft.ML.Data;
using SpotifyDataAnalysis.Modules.Prediction.Application.Training;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

// O ML.NET também tem um RegressionMetrics. O alias mantém explícito, em cada assinatura, qual dos dois está
// em jogo — o do domínio é o que atravessa a porta; o do framework morre aqui dentro.
using DomainMetrics = SpotifyDataAnalysis.Modules.Prediction.Domain.Training.RegressionMetrics;
using MlMetrics = Microsoft.ML.Data.RegressionMetrics;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

/// <summary>
/// O pipeline de treino e avaliação do ML.NET, isolado de onde os dados vêm.
///
/// <para>Recebe <c>IDataView</c> pronto e devolve métricas, o que permite ao teste de gate treinar sobre uma
/// fixture determinística sem tocar no banco — teste de qualidade que depende do estado do catálogo é flaky
/// por construção.</para>
/// </summary>
internal sealed class PopularityModelPipeline
{
    /// <summary>Nome do algoritmo campeão, ecoado no relatório.</summary>
    internal const string TrainerName = "FastTree";

    /// <summary>Folds da validação cruzada.</summary>
    internal const int CrossValidationFolds = 5;

    private const string FeaturesColumn = "Features";
    private const string LabelColumn = "Label";

    private readonly MLContext _mlContext;

    public PopularityModelPipeline(MLContext mlContext) => _mlContext = mlContext;

    /// <summary>
    /// Colunas que compõem o vetor de features nesta fatia: as 9 grandezas contínuas de áudio mais duração e
    /// explícito. Gênero, artista e <c>Key</c>/<c>Mode</c>/<c>TimeSignature</c> ficam de fora de propósito —
    /// são o E3.3, e o ganho deles precisa ser medido contra este ponto de partida.
    /// </summary>
    internal static readonly string[] FeatureColumns =
    [
        nameof(PopularityTrainingRow.Danceability),
        nameof(PopularityTrainingRow.Energy),
        nameof(PopularityTrainingRow.Valence),
        nameof(PopularityTrainingRow.Tempo),
        nameof(PopularityTrainingRow.Acousticness),
        nameof(PopularityTrainingRow.Instrumentalness),
        nameof(PopularityTrainingRow.Liveness),
        nameof(PopularityTrainingRow.Speechiness),
        nameof(PopularityTrainingRow.Loudness),
        nameof(PopularityTrainingRow.DurationMs),
        nameof(PopularityTrainingRow.Explicit)
    ];

    /// <summary>
    /// Treina o campeão sobre a visão de treino. A normalização não é enfeite: <c>Tempo</c> (BPM),
    /// <c>Loudness</c> (dB negativo) e <c>DurationMs</c> (centenas de milhares) conviveriam com features 0–1 e
    /// dominariam por escala.
    /// </summary>
    public ITransformer Train(IDataView trainingView)
    {
        return BuildEstimator(_mlContext.Regression.Trainers.FastTree(labelColumnName: LabelColumn))
            .Fit(trainingView);
    }

    /// <summary>Treina a regressão linear usada como segundo baseline.</summary>
    public ITransformer TrainLinearBaseline(IDataView trainingView)
    {
        // Sdca e não Ols: o Ols do ML.NET vem em Microsoft.ML.Mkl.Components, que arrasta binário nativo — a
        // mesma razão pela qual o LightGbm foi descartado como campeão (DP-1).
        return BuildEstimator(_mlContext.Regression.Trainers.Sdca(labelColumnName: LabelColumn))
            .Fit(trainingView);
    }

    /// <summary>Avalia um modelo treinado sobre a visão de TESTE.</summary>
    public DomainMetrics Evaluate(ITransformer model, IDataView testView)
    {
        IDataView predictions = model.Transform(testView);
        MlMetrics metrics = _mlContext.Regression.Evaluate(predictions, labelColumnName: LabelColumn);

        return new DomainMetrics(
            metrics.RSquared,
            metrics.MeanAbsoluteError,
            metrics.RootMeanSquaredError);
    }

    /// <summary>
    /// Métricas do baseline "prever sempre a média do TREINO". A média vem do treino, e não do teste, porque
    /// usar a média do próprio conjunto avaliado seria dar ao baseline uma informação que nenhum modelo tem em
    /// produção — e inflaria artificialmente o piso.
    /// </summary>
    public DomainMetrics EvaluateMeanBaseline(IDataView trainingView, IDataView testView)
    {
        double trainingMean = ReadLabels(trainingView).Average();

        return RegressionMetricsCalculator.CalculateForConstantPrediction(
            ReadLabels(testView), trainingMean);
    }

    /// <summary>
    /// Validação cruzada sobre o conjunto de TREINO, como leitura de estabilidade. O holdout dá um número; a
    /// dispersão entre folds diz se aquele número significa alguma coisa.
    /// </summary>
    public CrossValidationReport CrossValidate(IDataView trainingView)
    {
        // Tipo do resultado é um genérico aninhado do framework (TrainCatalogBase.CrossValidationResult<>);
        // nomeá-lo aqui só acrescentaria ruído.
        var results =
            _mlContext.Regression.CrossValidate(
                trainingView,
                BuildEstimator(_mlContext.Regression.Trainers.FastTree(labelColumnName: LabelColumn)),
                numberOfFolds: CrossValidationFolds,
                labelColumnName: LabelColumn);

        double[] rSquared = results.Select(result => result.Metrics.RSquared).ToArray();
        double[] meanAbsoluteError = results.Select(result => result.Metrics.MeanAbsoluteError).ToArray();

        return new CrossValidationReport(
            results.Count,
            rSquared.Average(),
            StandardDeviationOf(rSquared),
            meanAbsoluteError.Average(),
            StandardDeviationOf(meanAbsoluteError));
    }

    /// <summary>Quantas linhas a visão contém — o N que acompanha cada avaliação.</summary>
    public long CountRows(IDataView view) => ReadLabels(view).Count;

    private IEstimator<ITransformer> BuildEstimator(IEstimator<ITransformer> trainer) =>
        _mlContext.Transforms.Concatenate(FeaturesColumn, FeatureColumns)
            .Append(_mlContext.Transforms.NormalizeMinMax(FeaturesColumn))
            .Append(trainer);

    private IReadOnlyList<double> ReadLabels(IDataView view) =>
        _mlContext.Data
            .CreateEnumerable<LabelOnlyRow>(view, reuseRowObject: false)
            .Select(row => (double)row.Label)
            .ToArray();

    /// <summary>
    /// Desvio-padrão populacional: os folds são a população completa da medição, não uma amostra dela.
    /// </summary>
    private static double StandardDeviationOf(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        double mean = values.Average();

        return Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / values.Count);
    }

    /// <summary>Projeção mínima para ler o alvo sem materializar as features.</summary>
    private sealed class LabelOnlyRow
    {
        [ColumnName(LabelColumn)]
        public float Label { get; set; }
    }
}
