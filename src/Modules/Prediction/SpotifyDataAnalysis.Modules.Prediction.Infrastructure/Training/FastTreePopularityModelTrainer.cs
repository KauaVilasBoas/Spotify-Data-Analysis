using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using SpotifyDataAnalysis.Modules.Prediction.Application.Training;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.SharedKernel.Time;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

/// <summary>
/// Adaptador de treino: monta o dataset pelo mesmo caminho que o diagnóstico do E3.1 usa, treina o campeão e
/// os baselines, e devolve o relatório em tipos da Application — o ML.NET não atravessa a porta.
///
/// <para>Quando o treino exclui imputadas (o padrão), o modelo é avaliado <b>também</b> no conjunto ampliado
/// com elas. Isso é possível sem vazamento porque o split do E3.1 é determinístico por hash da faixa: uma
/// faixa que caiu no treino continua no treino quando as imputadas entram, então o conjunto ampliado só
/// acrescenta faixas de teste.</para>
/// </summary>
internal sealed class FastTreePopularityModelTrainer : IPopularityModelTrainer
{
    private const string MeasuredOnlyLabel = "measured-only";
    private const string IncludingImputedLabel = "including-imputed";

    private readonly MlNetTrainingDatasetProvider _datasetProvider;
    private readonly MLContext _mlContext;
    private readonly IModelVersionRepository _versionRepository;
    private readonly CurrentModelCache _modelCache;
    private readonly IClock _clock;
    private readonly ILogger<FastTreePopularityModelTrainer> _logger;

    /// <summary>
    /// Depende do provider CONCRETO, e não da porta: é ele que expõe as <c>IDataView</c>, enquanto a porta
    /// devolve só o censo. Os dois vivem nesta mesma Infrastructure, então a fronteira que importa — a
    /// Application não conhecer ML.NET — segue intacta.
    /// </summary>
    public FastTreePopularityModelTrainer(
        MlNetTrainingDatasetProvider datasetProvider,
        MLContext mlContext,
        IModelVersionRepository versionRepository,
        CurrentModelCache modelCache,
        IClock clock,
        ILogger<FastTreePopularityModelTrainer> logger)
    {
        _datasetProvider = datasetProvider;
        _mlContext = mlContext;
        _versionRepository = versionRepository;
        _modelCache = modelCache;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ModelTrainingReport> TrainAsync(
        TrainingDatasetSplitOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        long startedAt = Stopwatch.GetTimestamp();

        MlNetTrainingDataset dataset = await _datasetProvider.BuildDataViewsAsync(options, cancellationToken);

        var pipeline = new PopularityModelPipeline(_mlContext);

        ITransformer model = pipeline.Train(dataset.TrainingView);
        ITransformer linearBaseline = pipeline.TrainLinearBaseline(dataset.TrainingView);

        bool trainedOnImputed = options.ImputedFeaturePolicy == ImputedFeaturePolicy.IncludeImputed;

        ModelEvaluationReport primary = Evaluate(
            pipeline,
            trainedOnImputed ? IncludingImputedLabel : MeasuredOnlyLabel,
            model,
            linearBaseline,
            dataset.TrainingView,
            dataset.TestView);

        ModelEvaluationReport? imputedComparison = trainedOnImputed
            ? null
            : await EvaluateOnImputedComparisonAsync(
                pipeline, model, linearBaseline, dataset.TrainingView, options, cancellationToken);

        CrossValidationReport crossValidation = pipeline.CrossValidate(dataset.TrainingView);

        ModelPublicationReport publication = await PublishAsync(
            pipeline, model, dataset, options, trainedOnImputed, primary, cancellationToken);

        var report = new ModelTrainingReport(
            PopularityModelPipeline.TrainerName,
            PopularityModelPipeline.FeatureColumns,
            options.Seed,
            options.TestFraction,
            trainedOnImputed,
            dataset.Statistics.TrainingSampleCount,
            primary,
            imputedComparison,
            crossValidation,
            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            publication);

        LogOutcome(report);

        return report;
    }

    /// <summary>
    /// Serializa e registra a versão treinada, e aplica a política de promoção (DP-2). A versão é gravada
    /// SEMPRE — inclusive quando reprovada —, porque perder o registro de um treino ruim é perder a evidência
    /// de que ele aconteceu. Só a promoção é condicional.
    /// </summary>
    private async Task<ModelPublicationReport> PublishAsync(
        PopularityModelPipeline pipeline,
        ITransformer model,
        MlNetTrainingDataset dataset,
        TrainingDatasetSplitOptions options,
        bool trainedOnImputed,
        ModelEvaluationReport primary,
        CancellationToken cancellationToken)
    {
        byte[] artifact = pipeline.Serialize(model, dataset.TrainingView.Schema);
        string hash = Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant();

        ModelVersion version = ModelVersion.Register(
            _clock.UtcNow,
            PopularityModelPipeline.TrainerName,
            PopularityModelPipeline.FeatureColumns,
            options.Seed,
            options.TestFraction,
            dataset.Statistics.TrainingSampleCount,
            dataset.Statistics.TestSampleCount,
            trainedOnImputed,
            primary.Model,
            primary.MeanBaseline,
            artifact,
            hash);

        await _versionRepository.AddAsync(version, cancellationToken);

        ModelVersion? current = await _versionRepository.GetCurrentAsync(cancellationToken);

        ModelPromotionDecision decision = ModelPromotionPolicy.Decide(
            primary.Model, primary.MeanBaseline, current?.ModelMetrics);

        if (decision.ShouldPromote)
        {
            await _versionRepository.PromoteAsync(version, cancellationToken);

            // O cache aponta para a versão antiga; invalidar aqui é o que faz a promoção valer sem reiniciar.
            _modelCache.Invalidate();
        }

        return new ModelPublicationReport(
            version.Id, decision.ShouldPromote, decision.Reason, hash, artifact.LongLength);
    }

    /// <summary>
    /// Avalia o modelo treinado só com medidas no conjunto de teste ampliado com as imputadas. Remonta o
    /// dataset com a política invertida apenas para obter essa visão de teste — o modelo avaliado é o mesmo.
    /// </summary>
    private async Task<ModelEvaluationReport> EvaluateOnImputedComparisonAsync(
        PopularityModelPipeline pipeline,
        ITransformer model,
        ITransformer linearBaseline,
        IDataView trainingView,
        TrainingDatasetSplitOptions options,
        CancellationToken cancellationToken)
    {
        TrainingDatasetSplitOptions withImputed = TrainingDatasetSplitOptions.Create(
            options.Seed, options.TestFraction, ImputedFeaturePolicy.IncludeImputed);

        MlNetTrainingDataset ampliado =
            await _datasetProvider.BuildDataViewsAsync(withImputed, cancellationToken);

        return Evaluate(
            pipeline, IncludingImputedLabel, model, linearBaseline, trainingView, ampliado.TestView);
    }

    private static ModelEvaluationReport Evaluate(
        PopularityModelPipeline pipeline,
        string label,
        ITransformer model,
        ITransformer linearBaseline,
        IDataView trainingView,
        IDataView testView)
    {
        RegressionMetrics modelMetrics = pipeline.Evaluate(model, testView);
        RegressionMetrics meanBaseline = pipeline.EvaluateMeanBaseline(trainingView, testView);
        RegressionMetrics linearMetrics = pipeline.Evaluate(linearBaseline, testView);

        return new ModelEvaluationReport(
            label,
            pipeline.CountRows(testView),
            modelMetrics,
            meanBaseline,
            linearMetrics,
            ModelQualityGate.Evaluate(modelMetrics, meanBaseline));
    }

    private void LogOutcome(ModelTrainingReport report) =>
        _logger.LogInformation(
            "Treino {Trainer} (semente {Seed}): R² {RSquared:F4}, MAE {Mae:F3} contra MAE {BaselineMae:F3} do " +
            "baseline — melhora de {Improvement:P2}, gate {GateOutcome}. {Elapsed} ms.",
            report.Trainer,
            report.Seed,
            report.Primary.Model.RSquared,
            report.Primary.Model.MeanAbsoluteError,
            report.Primary.MeanBaseline.MeanAbsoluteError,
            report.Primary.Gate.MaeImprovement,
            report.Primary.Gate.Passed ? "APROVADO" : "REPROVADO",
            report.ElapsedMilliseconds);
}
