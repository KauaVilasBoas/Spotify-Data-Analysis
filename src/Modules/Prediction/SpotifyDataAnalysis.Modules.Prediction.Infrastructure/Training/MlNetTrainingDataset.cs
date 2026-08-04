using Microsoft.ML;
using SpotifyDataAnalysis.Modules.Prediction.Application.Training;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

/// <summary>
/// O dataset de treino já materializado como <c>IDataView</c>, com o censo e a medição que o produziram.
/// Tipo interno da Infrastructure: é aqui que o E3.2 pega as duas visões para treinar e avaliar, sem que o
/// <c>IDataView</c> vaze para a Application.
/// </summary>
/// <param name="TrainingView">Visão do conjunto de treino.</param>
/// <param name="TestView">Visão do conjunto de teste (holdout).</param>
/// <param name="Statistics">Censo do dataset.</param>
/// <param name="Measurement">Custo observado da montagem.</param>
internal sealed record MlNetTrainingDataset(
    IDataView TrainingView,
    IDataView TestView,
    TrainingDatasetStatistics Statistics,
    TrainingDatasetBuildMeasurement Measurement);
