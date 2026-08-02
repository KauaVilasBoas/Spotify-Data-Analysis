using Microsoft.ML;
using Microsoft.ML.Data;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

// O ML.NET também expõe um RegressionMetrics; o alias deixa explícito que aqui o tipo é o do domínio.
using DomainMetrics = SpotifyDataAnalysis.Modules.Prediction.Domain.Training.RegressionMetrics;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Training;

/// <summary>
/// Round-trip do artefato (E3.4): serializar, recarregar do <c>byte[]</c> e provar que a predição é
/// <b>idêntica</b>. Sem isto, o versionamento guardaria um binário que ninguém garante ser o modelo treinado.
/// </summary>
public sealed class ModelArtifactRoundTripTests
{
    private const int Seed = 20260730;

    private sealed class PopularityPrediction
    {
        [ColumnName("Score")]
        public float Score { get; set; }
    }

    [Fact]
    public void SerializedModel_ReloadedFromBytes_PredictsExactlyTheSameValues()
    {
        var mlContext = new MLContext(seed: Seed);
        var pipeline = new PopularityModelPipeline(mlContext);

        IReadOnlyList<TrackTrainingSample> samples = LearnableFixture.Create();
        IDataView training = mlContext.Data.LoadFromEnumerable(
            samples.Take(480).Select(PopularityTrainingRow.FromSample));
        IDataView test = mlContext.Data.LoadFromEnumerable(
            samples.Skip(480).Select(PopularityTrainingRow.FromSample));

        ITransformer trained = pipeline.Train(training, PopularityFeatureSet.Baseline);

        byte[] artifact = pipeline.Serialize(trained, training.Schema);

        using var stream = new MemoryStream(artifact);
        ITransformer reloaded = mlContext.Model.Load(stream, out _);

        float[] before = Predict(mlContext, trained, test);
        float[] after = Predict(mlContext, reloaded, test);

        Assert.Equal(before.Length, after.Length);
        Assert.True(before.Length > 0);

        for (int index = 0; index < before.Length; index++)
        {
            Assert.Equal(before[index], after[index]);
        }
    }

    [Fact]
    public void SerializedModel_IsNotEmpty_AndIsStableForTheSameSeed()
    {
        // O tamanho do artefato entra na ficha da versão; um binário vazio passaria despercebido sem isto.
        byte[] first = SerializeOnce();
        byte[] second = SerializeOnce();

        Assert.NotEmpty(first);
        Assert.Equal(first.Length, second.Length);

        static byte[] SerializeOnce()
        {
            var mlContext = new MLContext(seed: Seed);
            var pipeline = new PopularityModelPipeline(mlContext);

            IDataView training = mlContext.Data.LoadFromEnumerable(
                LearnableFixture.Create().Select(PopularityTrainingRow.FromSample));

            return pipeline.Serialize(pipeline.Train(training, PopularityFeatureSet.Baseline), training.Schema);
        }
    }

    [Fact]
    public void AVersionRegisteredWithTheArtifact_KeepsTheFeatureSetItWasTrainedWith()
    {
        // É o feature set gravado que permite recusar um modelo antigo quando o pipeline mudar (E3.3).
        var mlContext = new MLContext(seed: Seed);
        var pipeline = new PopularityModelPipeline(mlContext);

        IDataView training = mlContext.Data.LoadFromEnumerable(
            LearnableFixture.Create().Select(PopularityTrainingRow.FromSample));

        byte[] artifact = pipeline.Serialize(pipeline.Train(training, PopularityFeatureSet.Baseline), training.Schema);

        ModelVersion version = ModelVersion.Register(
            DateTime.UtcNow,
            PopularityModelPipeline.TrainerName,
            PopularityModelPipeline.BaselineFeatureColumns,
            Seed,
            0.2,
            480,
            120,
            trainedOnImputed: false,
            new DomainMetrics(0.15, 15, 19),
            new DomainMetrics(0, 17, 20),
            artifact,
            "hash");

        Assert.True(version.IsCompatibleWith(PopularityModelPipeline.BaselineFeatureColumns));
        Assert.NotEmpty(version.Artifact);
    }

    private static float[] Predict(MLContext mlContext, ITransformer model, IDataView data) =>
        mlContext.Data
            .CreateEnumerable<PopularityPrediction>(model.Transform(data), reuseRowObject: false)
            .Select(prediction => prediction.Score)
            .ToArray();
}
