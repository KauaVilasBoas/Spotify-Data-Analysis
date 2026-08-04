using System.Collections.Concurrent;
using Microsoft.ML;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Training;

/// <summary>
/// O pool sob concorrência (E3.5): o <c>PredictionEngine</c> não é thread-safe, então servir sem pool corrompe
/// resultado ou lança exceção intermitente sob carga. Estes testes disparam muitas predições paralelas e
/// exigem: nenhuma exceção, e o MESMO score para a MESMA entrada — a prova de que cada engine é usado por uma
/// thread de cada vez e o modelo não é compartilhado sem proteção.
/// </summary>
public sealed class PopularityPredictionEnginePoolConcurrencyTests
{
    private const int Seed = 20260730;

    private static (MLContext Context, ITransformer Model) TrainModel()
    {
        var mlContext = new MLContext(seed: Seed);
        var pipeline = new PopularityModelPipeline(mlContext);

        IDataView training = mlContext.Data.LoadFromEnumerable(
            LearnableFixture.Create().Select(PopularityTrainingRow.FromSample));

        return (mlContext, pipeline.Train(training, PopularityFeatureSet.Baseline));
    }

    private static PopularityTrainingRow SampleRow(int index) =>
        PopularityTrainingRow.FromAudioFeatures(
            danceability: (index % 100) / 100.0,
            energy: 0.6,
            valence: 0.4,
            tempo: 120.0,
            acousticness: 0.1,
            instrumentalness: 0.0,
            liveness: 0.2,
            speechiness: 0.05,
            loudness: -6.0,
            durationMs: 200_000,
            @explicit: false,
            key: index % 12,
            mode: index % 2,
            timeSignature: 4,
            genre: "fixture");

    [Fact]
    public async Task Rent_UnderParallelLoad_NeverThrows_AndIsDeterministicPerInput()
    {
        (MLContext mlContext, ITransformer model) = TrainModel();

        // Referência sequencial: o score esperado para cada uma das 100 entradas distintas.
        var expected = new float[100];
        using (var reference = new PopularityPredictionEnginePool(mlContext))
        {
            for (int index = 0; index < expected.Length; index++)
            {
                using PopularityPredictionEnginePool.EngineLease lease = reference.Rent(version: 1, model);
                expected[index] = lease.Engine.Predict(SampleRow(index)).Score;
            }
        }

        using var pool = new PopularityPredictionEnginePool(mlContext);
        var failures = new ConcurrentBag<string>();

        // 2000 predições paralelas sobre 100 entradas: contenção real no pool, engines emprestados e devolvidos.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, 2_000),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 4 },
            (iteration, _) =>
            {
                int input = iteration % 100;

                try
                {
                    using PopularityPredictionEnginePool.EngineLease lease = pool.Rent(version: 1, model);
                    float score = lease.Engine.Predict(SampleRow(input)).Score;

                    if (score != expected[input])
                        failures.Add($"input {input}: esperado {expected[input]}, obtido {score}");
                }
                catch (Exception ex)
                {
                    failures.Add($"exceção no input {input}: {ex.GetType().Name} — {ex.Message}");
                }

                return ValueTask.CompletedTask;
            });

        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Take(10)));
    }

    [Fact]
    public void Rent_AfterVersionChange_DrainsStaleEngines_AndServesTheNewModel()
    {
        (MLContext mlContext, ITransformer firstModel) = TrainModel();

        // Um segundo modelo, treinado só com metade dos dados, diverge do primeiro o suficiente para o teste
        // detectar se o pool serviu o modelo errado após a "promoção".
        var pipeline = new PopularityModelPipeline(mlContext);
        IDataView halfTraining = mlContext.Data.LoadFromEnumerable(
            LearnableFixture.Create(count: 300).Select(PopularityTrainingRow.FromSample));
        ITransformer secondModel = pipeline.Train(halfTraining, PopularityFeatureSet.Baseline);

        using var pool = new PopularityPredictionEnginePool(mlContext);

        float scoreV1;
        using (PopularityPredictionEnginePool.EngineLease leaseV1 = pool.Rent(version: 1, firstModel))
            scoreV1 = leaseV1.Engine.Predict(SampleRow(42)).Score;

        // "Promoção": mesma entrada, versão nova, modelo novo. O pool deve descartar os engines da v1 e servir
        // a v2 — se reutilizasse um engine antigo, o score seria o da v1.
        float scoreV2;
        using (PopularityPredictionEnginePool.EngineLease leaseV2 = pool.Rent(version: 2, secondModel))
            scoreV2 = leaseV2.Engine.Predict(SampleRow(42)).Score;

        float expectedV2 = mlContext.Model
            .CreatePredictionEngine<PopularityTrainingRow, PopularityScoreRow>(secondModel)
            .Predict(SampleRow(42)).Score;

        Assert.Equal(expectedV2, scoreV2);
        Assert.NotEqual(scoreV1, scoreV2);
    }
}
