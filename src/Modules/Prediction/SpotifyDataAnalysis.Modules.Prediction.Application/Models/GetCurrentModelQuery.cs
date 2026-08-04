using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;
using SpotifyDataAnalysis.SharedKernel.Exceptions;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Models;

/// <summary>
/// A ficha da versão do modelo que está respondendo as predições (E3.4).
/// </summary>
public sealed record GetCurrentModelQuery : IQuery<CurrentModelResult>;

/// <summary>
/// A versão corrente e o que a torna auditável: quando foi treinada, com que features, com que semente, sobre
/// quantas faixas, e com que métricas — as do modelo <b>e</b> as do baseline, porque uma métrica sozinha não
/// permite julgar nada.
/// </summary>
public sealed record CurrentModelResult(
    int Version,
    DateTime TrainedAtUtc,
    string Trainer,
    IReadOnlyList<string> Features,
    IReadOnlyList<FeatureImportanceResult> FeatureImportance,
    int Seed,
    double TestFraction,
    long TrainingSampleCount,
    long TestSampleCount,
    bool TrainedOnImputed,
    ModelMetricsResult Model,
    ModelMetricsResult Baseline,
    string ArtifactHash,
    long ArtifactSizeBytes);

/// <summary>Métricas de regressão no contrato público.</summary>
public sealed record ModelMetricsResult(
    double RSquared,
    double MeanAbsoluteError,
    double RootMeanSquaredError);

/// <summary>
/// Uma linha do ranking de importância de features no contrato público (E3.6), já ordenada da mais para a
/// menos importante e com os slots one-hot agregados sob o nome do bloco.
/// </summary>
/// <param name="Feature">Nome lógico da feature.</param>
/// <param name="SlotCount">Quantas colunas do vetor esta linha agrega (1 para feature escalar).</param>
/// <param name="RSquaredDropMean">Queda média de R² ao embaralhar a feature — o critério de ordenação.</param>
/// <param name="RSquaredDropStandardDeviation">Dispersão da queda de R² entre as permutações.</param>
/// <param name="MeanAbsoluteErrorIncreaseMean">Aumento médio do MAE, em pontos de popularidade.</param>
/// <param name="MeanAbsoluteErrorIncreaseStandardDeviation">Dispersão do aumento de MAE entre permutações.</param>
public sealed record FeatureImportanceResult(
    string Feature,
    int SlotCount,
    double RSquaredDropMean,
    double RSquaredDropStandardDeviation,
    double MeanAbsoluteErrorIncreaseMean,
    double MeanAbsoluteErrorIncreaseStandardDeviation);

internal sealed class GetCurrentModelQueryHandler : IQueryHandler<GetCurrentModelQuery, CurrentModelResult>
{
    private readonly IModelVersionRepository _repository;

    public GetCurrentModelQueryHandler(IModelVersionRepository repository) => _repository = repository;

    public async Task<CurrentModelResult> HandleAsync(
        GetCurrentModelQuery request, CancellationToken cancellationToken = default)
    {
        ModelVersionSummary? summary = await _repository.GetCurrentSummaryAsync(cancellationToken);

        // Sem modelo publicado, 404 explícito em ProblemDetails — nunca 200 com corpo vazio, que faria o
        // consumidor tratar "ainda não treinamos" como "o modelo não sabe responder".
        if (summary is null)
            throw new NotFoundException(
                "Nenhuma versão de modelo foi publicada ainda. Treine um modelo em POST /api/model/train.");

        return new CurrentModelResult(
            summary.Version,
            summary.TrainedAtUtc,
            summary.Trainer,
            summary.Features,
            summary.FeatureImportance.Select(ToResult).ToArray(),
            summary.Seed,
            summary.TestFraction,
            summary.TrainingSampleCount,
            summary.TestSampleCount,
            summary.TrainedOnImputed,
            new ModelMetricsResult(
                summary.ModelRSquared, summary.ModelMeanAbsoluteError, summary.ModelRootMeanSquaredError),
            new ModelMetricsResult(
                summary.BaselineRSquared,
                summary.BaselineMeanAbsoluteError,
                summary.BaselineRootMeanSquaredError),
            summary.ArtifactHash,
            summary.ArtifactSizeBytes);
    }

    private static FeatureImportanceResult ToResult(FeatureImportance importance) =>
        new(
            importance.Feature,
            importance.SlotCount,
            importance.RSquaredDrop.Mean,
            importance.RSquaredDrop.StandardDeviation,
            importance.MeanAbsoluteErrorIncrease.Mean,
            importance.MeanAbsoluteErrorIncrease.StandardDeviation);
}
