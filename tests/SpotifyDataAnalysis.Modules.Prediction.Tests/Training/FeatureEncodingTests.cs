using Microsoft.ML;
using Microsoft.ML.Data;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;
using RegressionMetrics = SpotifyDataAnalysis.Modules.Prediction.Domain.Training.RegressionMetrics;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Training;

/// <summary>
/// A codificação dos blocos A/B do E3.3, provada sem banco sobre a fixture determinística: Key/TimeSignature
/// viram texto categórico (nunca inteiro ordenado bruto), o gênero ausente cai no bucket sentinela, e o
/// pipeline com os blocos treina e prediz sem quebrar. Estes testes respondem "a codificação está correta por
/// tipo?" — pergunta diferente de "os blocos pagam no catálogo real?", que é a medição registrada no card.
/// </summary>
public sealed class FeatureEncodingTests
{
    private const int Seed = 20260730;

    [Fact]
    public void FromAudioFeatures_EncodesKeyAndTimeSignatureAsText_NotOrdinalInteger()
    {
        // Codificação por TIPO: Key é classe categórica, não grandeza ordenada. A prova é que o insumo do
        // one-hot é o texto da classe, e não o número cru — é isso que impede o modelo de tratar "Key 11" como
        // "maior que Key 1".
        PopularityTrainingRow row = PopularityTrainingRow.FromAudioFeatures(
            0.5, 0.6, 0.4, 120.0, 0.1, 0.0, 0.2, 0.05, -6.0, 200_000, false,
            key: 11, mode: 1, timeSignature: 3, genre: "pop");

        Assert.Equal("11", row.KeyCategory);
        Assert.Equal("3", row.TimeSignatureCategory);
        Assert.Equal(11f, row.Key);
        Assert.Equal(3f, row.TimeSignature);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromAudioFeatures_WithMissingGenre_MapsToTheUnknownBucket(string? genre)
    {
        // Comportamento definido para gênero ausente/desconhecido (critério do card): vira um bucket sentinela
        // conhecido, nunca uma string vazia que colidiria com "sem categoria" no one-hot.
        PopularityTrainingRow row = PopularityTrainingRow.FromAudioFeatures(
            0.5, 0.6, 0.4, 120.0, 0.1, 0.0, 0.2, 0.05, -6.0, 200_000, false,
            key: 0, mode: 0, timeSignature: 4, genre: genre);

        Assert.Equal(PopularityTrainingRow.UnknownGenre, row.GenreCategory);
    }

    [Fact]
    public void FromAudioFeatures_WithPresentGenre_KeepsIt()
    {
        PopularityTrainingRow row = PopularityTrainingRow.FromAudioFeatures(
            0.5, 0.6, 0.4, 120.0, 0.1, 0.0, 0.2, 0.05, -6.0, 200_000, false,
            key: 0, mode: 0, timeSignature: 4, genre: "afrobeat");

        Assert.Equal("afrobeat", row.GenreCategory);
    }

    [Fact]
    public void FullFeatureSet_ProducesAWiderFeatureVector_ThanTheBaseline()
    {
        // O one-hot dos blocos EXPANDE o vetor de features: se as colunas categóricas fossem ignoradas, a
        // largura seria a mesma da base. Contamos a dimensão da coluna Features após o fit de cada pipeline.
        (MLContext mlContext, IDataView training) = TrainingView();
        var pipeline = new PopularityModelPipeline(mlContext);

        int baselineWidth = FeatureVectorWidth(
            mlContext, pipeline.Train(training, PopularityFeatureSet.Baseline), training);
        int fullWidth = FeatureVectorWidth(
            mlContext,
            pipeline.Train(training, PopularityFeatureSet.NonContinuousAudio | PopularityFeatureSet.Genre),
            training);

        Assert.Equal(11, baselineWidth);
        Assert.True(
            fullWidth > baselineWidth,
            $"O vetor A+B ({fullWidth}) deveria ser mais largo que a base ({baselineWidth}) por causa do one-hot.");
    }

    [Fact]
    public void Genre_ProducesAWiderVector_ThanBaseline_ByExactlyOneHotOfDistinctGenres()
    {
        // A fixture usa um único gênero ("fixture"): +B adiciona exatamente 1 coluna one-hot. Isso prova que o
        // encoding é one-hot da CARDINALIDADE observada, não uma coluna fixa.
        (MLContext mlContext, IDataView training) = TrainingView();
        var pipeline = new PopularityModelPipeline(mlContext);

        int baselineWidth = FeatureVectorWidth(
            mlContext, pipeline.Train(training, PopularityFeatureSet.Baseline), training);
        int genreWidth = FeatureVectorWidth(
            mlContext, pipeline.Train(training, PopularityFeatureSet.Genre), training);

        Assert.Equal(baselineWidth + 1, genreWidth);
    }

    [Fact]
    public void Training_WithFullFeatureSet_IsReproducibleForTheSameSeed()
    {
        // Reprodutibilidade também com os blocos do E3.3: mesma seed → mesmas métricas até a última casa.
        RegressionMetrics first = TrainAndEvaluateFull();
        RegressionMetrics second = TrainAndEvaluateFull();

        Assert.Equal(first.MeanAbsoluteError, second.MeanAbsoluteError, precision: 12);
        Assert.Equal(first.RSquared, second.RSquared, precision: 12);
        Assert.Equal(first.RootMeanSquaredError, second.RootMeanSquaredError, precision: 12);
    }

    private static RegressionMetrics TrainAndEvaluateFull()
    {
        var mlContext = new MLContext(seed: Seed);
        var pipeline = new PopularityModelPipeline(mlContext);

        IReadOnlyList<TrackTrainingSample> samples = LearnableFixture.Create();
        IDataView training = mlContext.Data.LoadFromEnumerable(
            samples.Take(480).Select(PopularityTrainingRow.FromSample));
        IDataView test = mlContext.Data.LoadFromEnumerable(
            samples.Skip(480).Select(PopularityTrainingRow.FromSample));

        const PopularityFeatureSet full =
            PopularityFeatureSet.NonContinuousAudio | PopularityFeatureSet.Genre;

        return pipeline.Evaluate(pipeline.Train(training, full), test);
    }

    private static (MLContext, IDataView) TrainingView()
    {
        var mlContext = new MLContext(seed: Seed);
        IDataView training = mlContext.Data.LoadFromEnumerable(
            LearnableFixture.Create().Select(PopularityTrainingRow.FromSample));

        return (mlContext, training);
    }

    /// <summary>Largura da coluna vetorial <c>Features</c> após o fit — a dimensão que o trainer enxerga.</summary>
    private static int FeatureVectorWidth(MLContext mlContext, ITransformer model, IDataView data)
    {
        DataViewSchema schema = model.Transform(data).Schema;
        DataViewSchema.Column features = schema["Features"];

        return features.Type is VectorDataViewType vector
            ? vector.Size
            : throw new InvalidOperationException("A coluna Features deveria ser vetorial após a concatenação.");
    }
}
