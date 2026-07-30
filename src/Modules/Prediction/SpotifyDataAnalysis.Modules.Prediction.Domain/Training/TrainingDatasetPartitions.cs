namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// O dataset de treino já montado: as duas partições disjuntas e o censo que explica como se chegou nelas.
/// É o que a camada de infraestrutura transforma em <c>IDataView</c> do ML.NET — o domínio entrega listas
/// tipadas e não conhece o framework de ML.
/// </summary>
/// <param name="TrainingSamples">Amostras do conjunto de treino.</param>
/// <param name="TestSamples">Amostras do conjunto de teste (holdout).</param>
/// <param name="Statistics">O censo do dataset, incluindo os motivos de exclusão.</param>
public sealed record TrainingDatasetPartitions(
    IReadOnlyList<TrackTrainingSample> TrainingSamples,
    IReadOnlyList<TrackTrainingSample> TestSamples,
    TrainingDatasetStatistics Statistics);
