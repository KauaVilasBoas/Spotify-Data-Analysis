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
/// <para><b>E3.3 — eleição do campeão por medição.</b> Em vez de um único treino, o trainer avalia quatro
/// feature sets sobre o MESMO split/seed: a base do E3.2, cada bloco isolado (+A, +B) e a combinação (A+B). A
/// tabela comparativa e a régua de ≥2% por bloco (<see cref="FeatureBlockGainPolicy"/>) elegem o campeão; só
/// ele é serializado, versionado e submetido à promoção. Isso valida na prática o versionamento do E3.4:
/// quando um bloco paga, nasce uma versão nova com feature set diferente.</para>
///
/// <para>Quando o treino exclui imputadas (o padrão), o campeão é avaliado <b>também</b> no conjunto ampliado
/// com elas. Isso é possível sem vazamento porque o split do E3.1 é determinístico por hash da faixa: uma
/// faixa que caiu no treino continua no treino quando as imputadas entram, então o conjunto ampliado só
/// acrescenta faixas de teste.</para>
/// </summary>
internal sealed class FastTreePopularityModelTrainer : IPopularityModelTrainer
{
    private const string MeasuredOnlyLabel = "measured-only";
    private const string IncludingImputedLabel = "including-imputed";

    // A ordem em que os feature sets são avaliados e reportados: base primeiro (âncora), depois cada bloco, por
    // fim a combinação. É a mesma ordem da tabela que vai para o card.
    private static readonly PopularityFeatureSet[] EvaluatedFeatureSets =
    [
        PopularityFeatureSet.Baseline,
        PopularityFeatureSet.NonContinuousAudio,
        PopularityFeatureSet.Genre,
        PopularityFeatureSet.NonContinuousAudio | PopularityFeatureSet.Genre
    ];

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

        // Treina e avalia cada feature set sobre o MESMO split. É o coração da medição comparativa: nenhum
        // conjunto vê um dado que outro não viu, então a diferença de MAE é atribuível ao bloco, não ao acaso.
        IReadOnlyList<FeatureSetEvaluation> evaluations = EvaluatedFeatureSets
            .Select(featureSet => TrainAndScore(pipeline, featureSet, dataset))
            .ToArray();

        FeatureSetComparisonReport comparison = ElectChampion(evaluations);
        FeatureSetEvaluation champion = FindChampion(evaluations, comparison.ChampionLabel);

        bool trainedOnImputed = options.ImputedFeaturePolicy == ImputedFeaturePolicy.IncludeImputed;

        ModelEvaluationReport primary = Evaluate(
            pipeline,
            trainedOnImputed ? IncludingImputedLabel : MeasuredOnlyLabel,
            champion,
            dataset.TrainingView,
            dataset.TestView);

        ModelEvaluationReport? imputedComparison = trainedOnImputed
            ? null
            : await EvaluateOnImputedComparisonAsync(
                pipeline, champion, dataset.TrainingView, options, cancellationToken);

        CrossValidationReport crossValidation =
            pipeline.CrossValidate(dataset.TrainingView, champion.FeatureSet);

        FeatureImportanceReport featureImportance =
            new PermutationFeatureImportanceCalculator(_mlContext).Measure(champion.Model, dataset.TestView);

        ModelPublicationReport publication = await PublishAsync(
            pipeline, champion, dataset, options, trainedOnImputed, primary, featureImportance,
            cancellationToken);

        var report = new ModelTrainingReport(
            PopularityModelPipeline.TrainerName,
            PopularityFeatureSetDescriptor.LogicalFeatureNames(champion.FeatureSet),
            options.Seed,
            options.TestFraction,
            trainedOnImputed,
            dataset.Statistics.TrainingSampleCount,
            primary,
            imputedComparison,
            crossValidation,
            comparison,
            featureImportance,
            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            publication);

        LogOutcome(report);

        return report;
    }

    /// <summary>
    /// Treina o campeão de UM feature set e mede seu MAE/RMSE/R² no conjunto de teste. O modelo treinado é
    /// guardado junto das métricas para que o campeão eleito não precise ser retreinado — o custo do treino é
    /// pago uma vez por feature set.
    /// </summary>
    private FeatureSetEvaluation TrainAndScore(
        PopularityModelPipeline pipeline, PopularityFeatureSet featureSet, MlNetTrainingDataset dataset)
    {
        ITransformer model = pipeline.Train(dataset.TrainingView, featureSet);
        RegressionMetrics metrics = pipeline.Evaluate(model, dataset.TestView);

        return new FeatureSetEvaluation(featureSet, model, metrics);
    }

    /// <summary>
    /// Delega a régua do E3.3 à eleição de DOMÍNIO (<see cref="FeatureSetChampionElection"/>): traduz as
    /// avaliações de cada feature set em opções rotuladas e recebe de volta o campeão e o racional. A regra —
    /// bloco só permanece se pagar ≥2% isolado, com salvaguarda de menor MAE — mora no domínio, testável sem
    /// ML.NET; aqui só se faz a tradução ML.NET → rótulos e a resolução do rótulo do conjunto combinado.
    /// </summary>
    private static FeatureSetComparisonReport ElectChampion(IReadOnlyList<FeatureSetEvaluation> evaluations)
    {
        FeatureSetChampionElection.Option OptionOf(PopularityFeatureSet featureSet) =>
            new(PopularityFeatureSetDescriptor.Label(featureSet), MetricsOf(evaluations, featureSet));

        FeatureSetChampionElection.Result election = FeatureSetChampionElection.Elect(
            OptionOf(PopularityFeatureSet.Baseline),
            [
                OptionOf(PopularityFeatureSet.NonContinuousAudio),
                OptionOf(PopularityFeatureSet.Genre)
            ],
            EvaluatedFeatureSets.Select(OptionOf).ToArray(),
            ResolveCombinationLabel);

        return new FeatureSetComparisonReport(
            ToRow(evaluations, PopularityFeatureSet.Baseline),
            EvaluatedFeatureSets
                .Where(featureSet => featureSet != PopularityFeatureSet.Baseline)
                .Select(featureSet => ToRow(evaluations, featureSet))
                .ToArray(),
            election.ChampionLabel,
            election.Rationale,
            FeatureBlockGainPolicy.RequiredMaeGain);
    }

    /// <summary>
    /// Traduz o conjunto de rótulos de blocos aprovados (<c>+A</c>, <c>+B</c>) no rótulo do feature set a
    /// publicar. É o único ponto que conhece a NOMENCLATURA dos conjuntos — a regra de eleição a ignora.
    /// </summary>
    private static string ResolveCombinationLabel(IReadOnlyList<string> approvedBlockLabels)
    {
        PopularityFeatureSet combined = PopularityFeatureSet.Baseline;

        if (approvedBlockLabels.Contains(PopularityFeatureSetDescriptor.Label(PopularityFeatureSet.NonContinuousAudio)))
            combined |= PopularityFeatureSet.NonContinuousAudio;
        if (approvedBlockLabels.Contains(PopularityFeatureSetDescriptor.Label(PopularityFeatureSet.Genre)))
            combined |= PopularityFeatureSet.Genre;

        return PopularityFeatureSetDescriptor.Label(combined);
    }

    /// <summary>
    /// Serializa e registra a versão do CAMPEÃO, e aplica a política de promoção. A versão é gravada SEMPRE —
    /// inclusive quando reprovada —, porque perder o registro de um treino ruim é perder a evidência de que ele
    /// aconteceu. Só a promoção é condicional.
    /// </summary>
    private async Task<ModelPublicationReport> PublishAsync(
        PopularityModelPipeline pipeline,
        FeatureSetEvaluation champion,
        MlNetTrainingDataset dataset,
        TrainingDatasetSplitOptions options,
        bool trainedOnImputed,
        ModelEvaluationReport primary,
        FeatureImportanceReport featureImportance,
        CancellationToken cancellationToken)
    {
        byte[] artifact = pipeline.Serialize(champion.Model, dataset.TrainingView.Schema);
        string hash = Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant();

        ModelVersion version = ModelVersion.Register(
            _clock.UtcNow,
            PopularityModelPipeline.TrainerName,
            PopularityFeatureSetDescriptor.LogicalFeatureNames(champion.FeatureSet),
            featureImportance.Features,
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
    /// Avalia o campeão só-medidas no conjunto de teste ampliado com as imputadas. Remonta o dataset com a
    /// política invertida apenas para obter essa visão de teste — o modelo avaliado é o mesmo campeão.
    /// </summary>
    private async Task<ModelEvaluationReport> EvaluateOnImputedComparisonAsync(
        PopularityModelPipeline pipeline,
        FeatureSetEvaluation champion,
        IDataView trainingView,
        TrainingDatasetSplitOptions options,
        CancellationToken cancellationToken)
    {
        TrainingDatasetSplitOptions withImputed = TrainingDatasetSplitOptions.Create(
            options.Seed, options.TestFraction, ImputedFeaturePolicy.IncludeImputed);

        MlNetTrainingDataset ampliado =
            await _datasetProvider.BuildDataViewsAsync(withImputed, cancellationToken);

        return Evaluate(pipeline, IncludingImputedLabel, champion, trainingView, ampliado.TestView);
    }

    private static ModelEvaluationReport Evaluate(
        PopularityModelPipeline pipeline,
        string label,
        FeatureSetEvaluation champion,
        IDataView trainingView,
        IDataView testView)
    {
        RegressionMetrics modelMetrics = pipeline.Evaluate(champion.Model, testView);
        RegressionMetrics meanBaseline = pipeline.EvaluateMeanBaseline(trainingView, testView);

        // O baseline linear é retreinado com o feature set do campeão: o contraponto "árvore vs linha" só é
        // justo se ambos enxergarem as mesmas colunas.
        ITransformer linearBaseline = pipeline.TrainLinearBaseline(trainingView, champion.FeatureSet);
        RegressionMetrics linearMetrics = pipeline.Evaluate(linearBaseline, testView);

        return new ModelEvaluationReport(
            label,
            pipeline.CountRows(testView),
            modelMetrics,
            meanBaseline,
            linearMetrics,
            ModelQualityGate.Evaluate(modelMetrics, meanBaseline));
    }

    private static FeatureSetEvaluationRow ToRow(
        IReadOnlyList<FeatureSetEvaluation> evaluations, PopularityFeatureSet featureSet) =>
        new(
            PopularityFeatureSetDescriptor.Label(featureSet),
            PopularityFeatureSetDescriptor.LogicalFeatureNames(featureSet),
            MetricsOf(evaluations, featureSet));

    private static RegressionMetrics MetricsOf(
        IReadOnlyList<FeatureSetEvaluation> evaluations, PopularityFeatureSet featureSet) =>
        FindChampion(evaluations, PopularityFeatureSetDescriptor.Label(featureSet)).Metrics;

    private static FeatureSetEvaluation FindChampion(
        IReadOnlyList<FeatureSetEvaluation> evaluations, string label) =>
        evaluations.First(
            evaluation => PopularityFeatureSetDescriptor.Label(evaluation.FeatureSet) == label);

    private void LogOutcome(ModelTrainingReport report) =>
        _logger.LogInformation(
            "Treino {Trainer} (semente {Seed}): campeão {Champion}, R² {RSquared:F4}, MAE {Mae:F3} contra MAE " +
            "{BaselineMae:F3} do baseline — melhora de {Improvement:P2}, gate {GateOutcome}. Feature mais " +
            "importante: {TopFeature}. {Elapsed} ms no total, dos quais {ImportanceElapsed} ms de importância " +
            "sobre {SlotCount} slots.",
            report.Trainer,
            report.Seed,
            report.FeatureSetComparison.ChampionLabel,
            report.Primary.Model.RSquared,
            report.Primary.Model.MeanAbsoluteError,
            report.Primary.MeanBaseline.MeanAbsoluteError,
            report.Primary.Gate.MaeImprovement,
            report.Primary.Gate.Passed ? "APROVADO" : "REPROVADO",
            report.FeatureImportance.Features.FirstOrDefault()?.Feature ?? "n/d",
            report.ElapsedMilliseconds,
            report.FeatureImportance.ElapsedMilliseconds,
            report.FeatureImportance.SlotCount);

    /// <summary>Um feature set treinado e medido: o modelo e suas métricas de teste, guardados juntos.</summary>
    private sealed record FeatureSetEvaluation(
        PopularityFeatureSet FeatureSet, ITransformer Model, RegressionMetrics Metrics);
}
