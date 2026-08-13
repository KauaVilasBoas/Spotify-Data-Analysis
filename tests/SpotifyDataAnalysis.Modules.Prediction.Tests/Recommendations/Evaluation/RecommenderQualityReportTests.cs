using System.Diagnostics;
using System.Globalization;
using System.Text;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;
using Xunit.Abstractions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Evaluation;

/// <summary>
/// O relatório reproduzível do E4.4 (DP-1): roda os três proxies e a varredura de pesos do boost sobre o catálogo
/// REAL e imprime a tabela. Não é um teste de asserção — é o instrumento que produz os números que o card registra
/// e que o gate depois passa a defender. As asserções de regressão ficam em <see cref="RecommenderQualityGateTests"/>.
/// </summary>
[Collection(RecommenderEvaluationCollection.Name)]
public sealed class RecommenderQualityReportTests
{
    /// <summary>
    /// Os pesos varridos. A faixa desce até 0,02 de propósito: a suspeita registrada no card é de SATURAÇÃO — se a
    /// coerência ficar cravada em ~100% de 0,05 a 0,15, o peso está grande em toda a faixa e a comparação precisa
    /// de degraus mais baixos para mostrar onde o boost ainda desempata em vez de filtrar.
    /// </summary>
    private static readonly double[] SweptWeights = [0.02, 0.03, 0.04, 0.05, 0.06, 0.07, 0.08, 0.10, 0.15];

    private readonly RecommenderEvaluationHarness _harness;
    private readonly ITestOutputHelper _output;

    public RecommenderQualityReportTests(RecommenderEvaluationHarness harness, ITestOutputHelper output)
    {
        _harness = harness;
        _output = output;
    }

    [PostgresFact]
    public void Relatorio_dos_tres_proxies_e_da_calibracao_do_peso_de_genero()
    {
        var evaluator = new RecommenderQualityEvaluator();
        var report = new StringBuilder();

        long startTimestamp = Stopwatch.GetTimestamp();

        report.AppendLine(CultureInfo.InvariantCulture,
            $"CENSO: {_harness.Census.EligibleTracks} faixas elegiveis, {_harness.Census.ImputedTracks} imputadas.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"DUPLICATAS: {_harness.Census.DuplicateGroups} grupos, {_harness.Census.DuplicatedTracks} faixas, " +
            $"{_harness.Census.DuplicatePairs} pares, maior grupo {_harness.Census.LargestGroup}.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"AMOSTRA (seed '{RecommenderEvaluationHarness.SamplingSeed}'): " +
            $"{_harness.Sample.SeedTrackIds.Count} sementes, {_harness.Sample.DuplicateGroups.Count} grupos, " +
            $"top-N {RecommenderEvaluationHarness.TopN}.");
        report.AppendLine();
        report.AppendLine("config       | coerencia | saturacao | sementes | sem_genero | fora_indice | imputadas | autoexcl_viol");

        var settings = new List<RecommenderEvaluationSetting> { RecommenderEvaluationSetting.CosineOnly() };
        foreach (double weight in SweptWeights)
            settings.Add(RecommenderEvaluationSetting.BoostedBy(weight));

        foreach (RecommenderEvaluationSetting setting in settings)
        {
            RecommenderQualityMeasurement measurement = evaluator.Measure(
                _harness.Index, _harness.Sample, setting, RecommenderEvaluationHarness.TopN);

            GenreCoherenceProxy coherence = measurement.GenreCoherence;

            report.AppendLine(CultureInfo.InvariantCulture,
                $"{setting.Label,-12} | {coherence.MeanCoherence,9:0.0000} | {coherence.SaturationRate,9:0.0000} | " +
                $"{coherence.SeedsEvaluated,8} | {coherence.SeedsWithoutUsableGenre,10} | " +
                $"{coherence.SeedsMissingFromIndex,11} | {coherence.ImputedSeeds,9} | " +
                $"{measurement.SelfExclusion.Violations,13}");
        }

        DuplicateProximityProxy duplicates = evaluator.MeasureDuplicateProximity(
            _harness.Index, _harness.Sample, RecommenderEvaluationHarness.TopN);

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"PROXY 3 (genreMode=off, top-K {RecommenderEvaluationHarness.TopN}): " +
            $"grupos {duplicates.GroupsEvaluated}, sementes {duplicates.SeedsEvaluated}, " +
            $"recall {duplicates.MeanRecall:0.0000}, hit-rate {duplicates.HitRate:0.0000}, " +
            $"1o irmao na posicao media {duplicates.MeanFirstSiblingRank:0.00}, " +
            $"cosine medio dos irmaos {duplicates.MeanSiblingCosine:0.000000}, " +
            $"top-K 100% duplicado em {duplicates.SeedsWithFullyDuplicatedTopK} sementes.");

        TimeSpan evaluationDuration = Stopwatch.GetElapsedTime(startTimestamp);

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"CUSTO: indice montado em {_harness.IndexBuildDuration.TotalSeconds:0.00} s " +
            $"({_harness.IndexFootprintMb:0.0} MB retidos), avaliacao em {evaluationDuration.TotalSeconds:0.00} s, " +
            $"pico de working set {_harness.PeakWorkingSetMb:0.0} MB.");

        _output.WriteLine(report.ToString());
    }
}
