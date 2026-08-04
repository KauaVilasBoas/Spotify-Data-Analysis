using Microsoft.Extensions.Options;
using SpotifyDataAnalysis.Modules.Prediction.Application.Training;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Dataset;

/// <summary>
/// Diagnóstico do dataset de treino (E3.1): com quantas faixas dá para treinar, quantas ficaram de fora e
/// por quê. Sem esse número honesto na mesa, treinar é chute.
///
/// <para>A query monta o dataset de verdade — o mesmo caminho que o treino do E3.2 vai usar — em vez de
/// estimar as contagens por uma agregação paralela em SQL. É uma escolha deliberada: dois caminhos divergem
/// com o tempo, e o dia em que divergirem o diagnóstico estará mentindo justamente sobre o dataset que o
/// modelo recebeu.</para>
/// </summary>
public sealed record GetTrainingDatasetStatsQuery : IQuery<TrainingDatasetStatsResult>
{
    private readonly double? _testFraction;

    /// <summary>
    /// Semente do split. Ausente usa a semente de configuração — que é a semente com a qual os modelos do
    /// projeto são treinados e, portanto, a única que produz um diagnóstico comparável com eles.
    /// </summary>
    public int? Seed { get; init; }

    /// <summary>
    /// Fração reservada para teste. Ausente usa a de configuração; presente, é presa ao intervalo aceito pelo
    /// domínio, para um valor absurdo virar um recorte válido e explícito em vez de erro 500.
    /// </summary>
    public double? TestFraction
    {
        get => _testFraction;
        init => _testFraction = value is null
            ? null
            : Math.Clamp(
                value.Value,
                TrainingDatasetSplitOptions.MinimumTestFraction,
                TrainingDatasetSplitOptions.MaximumTestFraction);
    }

    /// <summary>
    /// Se as faixas com features IMPUTADAS entram no conjunto. O padrão é <c>false</c> (DP-2): a imputação
    /// pela mediana estratificada por gênero comprime a variância e cria uma correlação artificial
    /// feature → gênero → popularidade, e treinar nisso ensina o modelo a reconhecer a mediana do gênero, não
    /// a música. Ligar a flag serve para comparar os dois recortes, não para virar o padrão.
    /// </summary>
    public bool IncludeImputed { get; init; }
}

/// <summary>
/// O censo do dataset de treino. As contagens fecham: o total do catálogo é a soma das três exclusões
/// estruturais com o conjunto estruturalmente elegível, e este é a soma do medido com o imputado.
/// </summary>
/// <param name="TotalTracks">Total de faixas no catálogo.</param>
/// <param name="ExcludedMissingPopularity">Excluídas por não terem popularidade (sem alvo).</param>
/// <param name="ExcludedMissingAudioFeatures">Excluídas por não terem audio-features (jsonb nulo).</param>
/// <param name="ExcludedIncompleteAudioFeatures">Excluídas por faltar alguma grandeza numérica no jsonb.</param>
/// <param name="EligibleWithMeasuredFeatures">Elegíveis com features MEDIDAS.</param>
/// <param name="EligibleWithImputedFeatures">Elegíveis com features IMPUTADAS.</param>
/// <param name="EligibleIncludingImputed">Elegíveis somando medidas e imputadas — o conjunto de comparação da DP-2.</param>
/// <param name="ExcludedByImputationPolicy">Elegíveis descartadas pela política de imputação em vigor.</param>
/// <param name="TrainableTracks">Faixas efetivamente particionadas (treino + teste).</param>
/// <param name="TrainingSampleCount">Tamanho do conjunto de treino após o split.</param>
/// <param name="TestSampleCount">Tamanho do conjunto de teste após o split.</param>
/// <param name="IncludedImputed">Ecoa o recorte de imputação aplicado.</param>
/// <param name="Seed">Semente usada no split.</param>
/// <param name="TestFraction">Fração de teste pedida ao split.</param>
/// <param name="Measurement">Custo observado da montagem (tempo e memória).</param>
public sealed record TrainingDatasetStatsResult(
    long TotalTracks,
    long ExcludedMissingPopularity,
    long ExcludedMissingAudioFeatures,
    long ExcludedIncompleteAudioFeatures,
    long EligibleWithMeasuredFeatures,
    long EligibleWithImputedFeatures,
    long EligibleIncludingImputed,
    long ExcludedByImputationPolicy,
    long TrainableTracks,
    long TrainingSampleCount,
    long TestSampleCount,
    bool IncludedImputed,
    int Seed,
    double TestFraction,
    TrainingDatasetBuildMeasurement Measurement);

internal sealed class GetTrainingDatasetStatsQueryHandler
    : IQueryHandler<GetTrainingDatasetStatsQuery, TrainingDatasetStatsResult>
{
    private readonly ITrainingDatasetProvider _datasetProvider;
    private readonly TrainingDatasetSettings _settings;

    public GetTrainingDatasetStatsQueryHandler(
        ITrainingDatasetProvider datasetProvider,
        IOptions<TrainingDatasetSettings> settings)
    {
        _datasetProvider = datasetProvider;
        _settings = settings.Value;
    }

    public async Task<TrainingDatasetStatsResult> HandleAsync(
        GetTrainingDatasetStatsQuery request, CancellationToken cancellationToken = default)
    {
        TrainingDatasetSplitOptions options = TrainingDatasetSplitOptions.Create(
            request.Seed ?? _settings.Seed,
            request.TestFraction ?? _settings.TestFraction,
            request.IncludeImputed
                ? ImputedFeaturePolicy.IncludeImputed
                : ImputedFeaturePolicy.ExcludeImputed);

        TrainingDatasetBuildResult build = await _datasetProvider.BuildAsync(options, cancellationToken);

        return Map(build);
    }

    private static TrainingDatasetStatsResult Map(TrainingDatasetBuildResult build)
    {
        TrainingDatasetStatistics statistics = build.Statistics;

        return new TrainingDatasetStatsResult(
            statistics.TotalTracks,
            statistics.ExcludedMissingPopularity,
            statistics.ExcludedMissingAudioFeatures,
            statistics.ExcludedIncompleteAudioFeatures,
            statistics.EligibleWithMeasuredFeatures,
            statistics.EligibleWithImputedFeatures,
            statistics.StructurallyEligible,
            statistics.ExcludedByImputationPolicy,
            statistics.TrainableTracks,
            statistics.TrainingSampleCount,
            statistics.TestSampleCount,
            statistics.AppliedImputedFeaturePolicy == ImputedFeaturePolicy.IncludeImputed,
            statistics.Seed,
            statistics.TestFraction,
            build.Measurement);
    }
}
