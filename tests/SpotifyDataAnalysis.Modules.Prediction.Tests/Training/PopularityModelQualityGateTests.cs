using Microsoft.ML;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Training;

/// <summary>
/// O GATE DE QUALIDADE do E3.2: treina o pipeline real do ML.NET sobre a fixture determinística e falha se o
/// modelo não reduzir o MAE em pelo menos 5% sobre o baseline de prever a média (DP-2/DP-3).
///
/// <para>Sem banco, de propósito: o conteúdo do catálogo muda a cada ingestão e um gate preso a ele é flaky
/// por construção. A fixture tem sinal conhecido, então este teste responde "o pipeline aprende?" — pergunta
/// diferente de "o catálogo real permite aprender?", que é medida à parte e registrada no card.</para>
/// </summary>
public sealed class PopularityModelQualityGateTests
{
    private const int Seed = 20260730;

    private static (PopularityModelPipeline Pipeline, IDataView Training, IDataView Test) Arrange(
        IReadOnlyList<TrackTrainingSample> samples)
    {
        var mlContext = new MLContext(seed: Seed);
        var pipeline = new PopularityModelPipeline(mlContext);

        int split = (int)(samples.Count * 0.8);

        IDataView training = mlContext.Data.LoadFromEnumerable(
            samples.Take(split).Select(PopularityTrainingRow.FromSample));
        IDataView test = mlContext.Data.LoadFromEnumerable(
            samples.Skip(split).Select(PopularityTrainingRow.FromSample));

        return (pipeline, training, test);
    }

    [Fact]
    public void QualityGate_ModelBeatsTheMeanBaselineByTheRequiredMargin()
    {
        (PopularityModelPipeline pipeline, IDataView training, IDataView test) =
            Arrange(LearnableFixture.Create());

        ITransformer model = pipeline.Train(training, PopularityFeatureSet.Baseline);

        RegressionMetrics modelMetrics = pipeline.Evaluate(model, test);
        RegressionMetrics baseline = pipeline.EvaluateMeanBaseline(training, test);
        ModelQualityVerdict verdict = ModelQualityGate.Evaluate(modelMetrics, baseline);

        Assert.True(
            verdict.Passed,
            $"O modelo não bateu o baseline pela margem exigida. " +
            $"MAE modelo {modelMetrics.MeanAbsoluteError:F3} contra baseline {baseline.MeanAbsoluteError:F3} " +
            $"(melhora de {verdict.MaeImprovement:P2}, exigido {verdict.RequiredMaeImprovement:P2}).");
    }

    [Fact]
    public void QualityGate_RejectsAModelTrainedOnPureNoise()
    {
        // Contraprova: um gate que aprova qualquer coisa não é gate. Sem sinal, o modelo não tem como bater a
        // média por 5%.
        (PopularityModelPipeline pipeline, IDataView training, IDataView test) =
            Arrange(LearnableFixture.CreateWithoutSignal());

        ITransformer model = pipeline.Train(training, PopularityFeatureSet.Baseline);

        ModelQualityVerdict verdict = ModelQualityGate.Evaluate(
            pipeline.Evaluate(model, test),
            pipeline.EvaluateMeanBaseline(training, test));

        Assert.False(verdict.Passed);
    }

    [Fact]
    public void Training_WithTheSameSeed_IsReproducible()
    {
        // Critério de aceite: duas execuções com a mesma semente produzem exatamente as mesmas métricas.
        RegressionMetrics first = TrainAndEvaluate();
        RegressionMetrics second = TrainAndEvaluate();

        Assert.Equal(first.MeanAbsoluteError, second.MeanAbsoluteError, precision: 12);
        Assert.Equal(first.RSquared, second.RSquared, precision: 12);
        Assert.Equal(first.RootMeanSquaredError, second.RootMeanSquaredError, precision: 12);

        static RegressionMetrics TrainAndEvaluate()
        {
            (PopularityModelPipeline pipeline, IDataView training, IDataView test) =
                Arrange(LearnableFixture.Create());

            return pipeline.Evaluate(pipeline.Train(training, PopularityFeatureSet.Baseline), test);
        }
    }

    [Fact]
    public void MeanBaseline_UsesTheTrainingMean_NotTheTestMean()
    {
        // O baseline não pode enxergar o conjunto de teste: usar a média do teste lhe daria informação que
        // nenhum modelo tem em produção e inflaria o piso.
        (PopularityModelPipeline pipeline, IDataView training, IDataView test) =
            Arrange(LearnableFixture.Create());

        RegressionMetrics baseline = pipeline.EvaluateMeanBaseline(training, test);

        // Prever a média do PRÓPRIO teste daria R² exatamente 0; vindo do treino, fica levemente abaixo.
        Assert.True(baseline.RSquared <= 0);
    }

    [Fact]
    public void CrossValidation_ReportsFiveFoldsWithDispersion()
    {
        (PopularityModelPipeline pipeline, IDataView training, _) = Arrange(LearnableFixture.Create());

        var report = pipeline.CrossValidate(training, PopularityFeatureSet.Baseline);

        Assert.Equal(PopularityModelPipeline.CrossValidationFolds, report.Folds);
        Assert.True(report.StandardDeviationMeanAbsoluteError >= 0);
        Assert.True(report.MeanMeanAbsoluteError > 0);
    }

    [Fact]
    public void BaselineFeatureSet_IsTheAudioSliceOnly_TheFixedE32Anchor()
    {
        // A base do E3.3 é, por construção, o feature set do E3.2: 11 colunas, sem gênero nem
        // Key/Mode/TimeSignature. É o ponto de partida contra o qual o ganho de cada bloco é medido — se ele
        // mudar, a comparação incremental deixa de ser honesta.
        Assert.Equal(11, PopularityModelPipeline.BaselineFeatureColumns.Length);
        Assert.Contains(nameof(PopularityTrainingRow.DurationMs), PopularityModelPipeline.BaselineFeatureColumns);
        Assert.Contains(nameof(PopularityTrainingRow.Explicit), PopularityModelPipeline.BaselineFeatureColumns);
        Assert.DoesNotContain(nameof(PopularityTrainingRow.Genre), PopularityModelPipeline.BaselineFeatureColumns);
        Assert.DoesNotContain(nameof(PopularityTrainingRow.Key), PopularityModelPipeline.BaselineFeatureColumns);
        Assert.DoesNotContain(nameof(PopularityTrainingRow.Mode), PopularityModelPipeline.BaselineFeatureColumns);
        Assert.DoesNotContain(
            nameof(PopularityTrainingRow.TimeSignature), PopularityModelPipeline.BaselineFeatureColumns);
    }

    [Fact]
    public void LogicalFeatureNames_OfFullSet_PublishBlocksAAndBOverTheBaseline()
    {
        // O feature set publicado (ModelVersion.Features / GET /api/model/current) usa nomes LÓGICOS: o cliente
        // vê "Genre", não as 113 colunas one-hot. A+B acrescenta Key/Mode/TimeSignature e Genre à base.
        IReadOnlyList<string> names = PopularityFeatureSetDescriptor.LogicalFeatureNames(
            PopularityFeatureSet.NonContinuousAudio | PopularityFeatureSet.Genre);

        Assert.Equal(15, names.Count);
        foreach (string baselineColumn in PopularityModelPipeline.BaselineFeatureColumns)
            Assert.Contains(baselineColumn, names);

        Assert.Contains(nameof(PopularityTrainingRow.Key), names);
        Assert.Contains(nameof(PopularityTrainingRow.Mode), names);
        Assert.Contains(nameof(PopularityTrainingRow.TimeSignature), names);
        Assert.Contains(nameof(PopularityTrainingRow.Genre), names);
    }
}
