using Microsoft.ML;
using Microsoft.ML.Data;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Training;

/// <summary>
/// O teste que fecha o risco mais caro do épico (E3.5): <b>skew treino/inferência</b>. Prova que o score que a
/// inferência produz — engine do pool alimentado por <c>PopularityTrainingRow.FromAudioFeatures</c> — é
/// <b>idêntico</b> ao que o pipeline de avaliação (<c>model.Transform</c> sobre <c>FromSample</c>) dá para a
/// mesma faixa. Se a montagem de features divergir em ordem, normalização ou conversão, o endpoint responderia
/// números plausíveis e errados sem erro nenhum — este teste é a rede que pega isso.
/// </summary>
public sealed class TrainInferenceEquivalenceTests
{
    private const int Seed = 20260730;

    private sealed class ScoreRow
    {
        [ColumnName("Score")]
        public float Score { get; set; }
    }

    [Fact]
    public void EnginePool_PredictsExactlyTheSameScore_AsTheEvaluationPipeline()
    {
        var mlContext = new MLContext(seed: Seed);
        var pipeline = new PopularityModelPipeline(mlContext);

        IReadOnlyList<TrackTrainingSample> samples = LearnableFixture.Create();
        IReadOnlyList<TrackTrainingSample> trainingSamples = samples.Take(480).ToArray();
        IReadOnlyList<TrackTrainingSample> testSamples = samples.Skip(480).ToArray();

        IDataView trainingView = mlContext.Data.LoadFromEnumerable(
            trainingSamples.Select(PopularityTrainingRow.FromSample));

        ITransformer model = pipeline.Train(trainingView);

        // Lado do TREINO/AVALIAÇÃO: os scores como o pipeline de avaliação os produz, sobre FromSample.
        float[] pipelineScores = mlContext.Data
            .CreateEnumerable<ScoreRow>(
                model.Transform(mlContext.Data.LoadFromEnumerable(
                    testSamples.Select(PopularityTrainingRow.FromSample))),
                reuseRowObject: false)
            .Select(row => row.Score)
            .ToArray();

        // Lado da INFERÊNCIA: o pool próprio, alimentado pela MESMA rotina de montagem que o endpoint usa
        // (FromAudioFeatures), faixa a faixa.
        using var pool = new PopularityPredictionEnginePool(mlContext);

        float[] inferenceScores = testSamples
            .Select(sample =>
            {
                using PopularityPredictionEnginePool.EngineLease lease = pool.Rent(version: 1, model);

                PopularityTrainingRow row = PopularityTrainingRow.FromAudioFeatures(
                    sample.Danceability, sample.Energy, sample.Valence, sample.Tempo, sample.Acousticness,
                    sample.Instrumentalness, sample.Liveness, sample.Speechiness, sample.Loudness,
                    sample.DurationMs, sample.Explicit);

                return lease.Engine.Predict(row).Score;
            })
            .ToArray();

        Assert.Equal(pipelineScores.Length, inferenceScores.Length);
        Assert.True(pipelineScores.Length > 0);

        for (int index = 0; index < pipelineScores.Length; index++)
        {
            // Igualdade EXATA (bit a bit): o mesmo modelo e a mesma linha têm de produzir o mesmo float. Uma
            // tolerância aqui esconderia justamente o tipo de divergência que este teste existe para pegar.
            Assert.Equal(pipelineScores[index], inferenceScores[index]);
        }
    }
}
