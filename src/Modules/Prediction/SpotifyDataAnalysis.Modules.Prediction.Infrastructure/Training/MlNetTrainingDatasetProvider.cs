using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using SpotifyDataAnalysis.Modules.Prediction.Application.Training;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

/// <summary>
/// Adaptador que monta o dataset de treino: lê o catálogo em lotes pela porta
/// <see cref="ITrackTrainingCandidateSource"/>, deixa o domínio decidir elegibilidade e partição, e só então
/// materializa as duas partições como <c>IDataView</c> do ML.NET.
///
/// <para>A carga usa <c>LoadFromEnumerable</c> sobre uma projeção PREGUIÇOSA das amostras do domínio, e não
/// uma cópia materializada em memória: o <c>IDataView</c> passa a ser uma janela sobre as listas que já
/// existem, em vez de um segundo dataset ao lado do primeiro. Quem precisar de várias passadas rápidas sobre
/// os dados (o treino do E3.2) decide se vale pagar um <c>Data.Cache</c> — a decisão fica com quem treina,
/// não escondida aqui.</para>
///
/// <para>A montagem é instrumentada de propósito. O projeto roda em free tier (256–512 MB), então "quanto o
/// dataset ocupa" é critério de desenho: o número medido aqui é o que diz se o treino pode acontecer no host
/// da demo ou se o modelo tem de ser treinado localmente e publicado pronto.</para>
/// </summary>
internal sealed class MlNetTrainingDatasetProvider : ITrainingDatasetProvider
{
    private readonly ITrackTrainingCandidateSource _candidateSource;
    private readonly MLContext _mlContext;
    private readonly TrainingDatasetSettings _settings;
    private readonly ILogger<MlNetTrainingDatasetProvider> _logger;

    public MlNetTrainingDatasetProvider(
        ITrackTrainingCandidateSource candidateSource,
        MLContext mlContext,
        IOptions<TrainingDatasetSettings> settings,
        ILogger<MlNetTrainingDatasetProvider> logger)
    {
        _candidateSource = candidateSource;
        _mlContext = mlContext;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TrainingDatasetBuildResult> BuildAsync(
        TrainingDatasetSplitOptions options,
        CancellationToken cancellationToken = default)
    {
        MlNetTrainingDataset dataset = await BuildDataViewsAsync(options, cancellationToken);

        return new TrainingDatasetBuildResult(dataset.Statistics, dataset.Measurement);
    }

    /// <summary>
    /// Monta o dataset e devolve também as visões do ML.NET. É por aqui que o treino do E3.2 entra: um só
    /// caminho de montagem, então o dataset que o modelo recebe é exatamente o que o endpoint de diagnóstico
    /// descreve.
    /// </summary>
    internal async Task<MlNetTrainingDataset> BuildDataViewsAsync(
        TrainingDatasetSplitOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var assembler = new TrainingDatasetAssembler(
            options,
            new TrainingEligibilitySpecification(options.ImputedFeaturePolicy),
            new SeededHashTrainTestSplitStrategy(options));

        long retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        long startedAt = Stopwatch.GetTimestamp();

        await foreach (TrackTrainingCandidate candidate in
                       _candidateSource.StreamCandidatesAsync(_settings.ReadBatchSize, cancellationToken))
        {
            assembler.Accept(candidate);
        }

        TrainingDatasetPartitions partitions = assembler.Build();

        IDataView trainingView = LoadDataView(partitions.TrainingSamples);
        IDataView testView = LoadDataView(partitions.TestSamples);

        TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;
        long retainedBytes = Math.Max(GC.GetTotalMemory(forceFullCollection: true) - retainedBefore, 0);

        var measurement = new TrainingDatasetBuildMeasurement(
            (long)elapsed.TotalMilliseconds, allocatedBytes, retainedBytes);

        LogMeasurement(partitions.Statistics, measurement);

        return new MlNetTrainingDataset(trainingView, testView, partitions.Statistics, measurement);
    }

    private IDataView LoadDataView(IReadOnlyList<TrackTrainingSample> samples) =>
        _mlContext.Data.LoadFromEnumerable(samples.Select(PopularityTrainingRow.FromSample));

    private void LogMeasurement(
        TrainingDatasetStatistics statistics,
        TrainingDatasetBuildMeasurement measurement) =>
        _logger.LogInformation(
            "Dataset de treino montado: {TrainingCount} treino / {TestCount} teste de {TotalTracks} faixas " +
            "(política de imputação {ImputedPolicy}, semente {Seed}) em {ElapsedMs} ms, " +
            "{RetainedBytes} bytes retidos, {AllocatedBytes} bytes alocados.",
            statistics.TrainingSampleCount,
            statistics.TestSampleCount,
            statistics.TotalTracks,
            statistics.AppliedImputedFeaturePolicy,
            statistics.Seed,
            measurement.ElapsedMilliseconds,
            measurement.RetainedBytes,
            measurement.AllocatedBytes);
}
