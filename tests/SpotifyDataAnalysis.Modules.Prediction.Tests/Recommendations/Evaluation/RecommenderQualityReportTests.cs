using System.Diagnostics;
using System.Globalization;
using System.Text;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;
using Xunit.Abstractions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Evaluation;

/// <summary>
/// O relatório reproduzível da avaliação do recomendador: roda os proxies sobre o catálogo REAL e imprime a tabela.
/// Não é um teste de asserção — é o instrumento que produz os números que o card registra e que o gate depois passa
/// a defender. As asserções de regressão ficam em <see cref="RecommenderQualityGateTests"/>.
///
/// <para><b>Regra do E4.8 que este relatório existe para cumprir:</b> toda linha sai com a CONFIGURAÇÃO que a
/// produziu (<c>genreMode</c>, <c>dedupe</c>, <c>strategy</c>, <c>blendWeight</c>). Foi a ausência disso que
/// deixou os números do E4.4 descreverem, por dois épicos, um sistema que já não era o default do endpoint.</para>
/// </summary>
[Collection(RecommenderEvaluationCollection.Name)]
public sealed class RecommenderQualityReportTests
{
    /// <summary>
    /// Os pesos varridos na calibração de gênero do E4.4, preservados para a comparação pareada continuar existindo.
    /// A faixa desce até 0,02 de propósito: a suspeita registrada no card era de SATURAÇÃO, e mostrar onde o boost
    /// ainda desempata em vez de filtrar exige degraus mais baixos.
    /// </summary>
    private static readonly double[] SweptWeights = [0.02, 0.03, 0.04, 0.05, 0.06, 0.07, 0.08, 0.10, 0.15];

    /// <summary>Os pesos do blend colaborativo medidos sobre o RANKING FINAL (E4.8): o default e um contraste alto.</summary>
    private static readonly double[] BlendWeights = [0.35, 0.60];

    private const string CoherenceHeader =
        "config                                 | coerencia |  desvio |  erro_padrao | saturacao | sementes | sem_genero | fora_indice | imputadas | autoexcl_viol";

    private const string DuplicateHeader =
        "amostra                    | config                          | grupos | sementes | recall | hit-rate | 1o_irmao | cos_irmaos | topK_100%_dup | repeticao_no_topK | topK_incompleto";

    private readonly RecommenderEvaluationHarness _harness;
    private readonly ITestOutputHelper _output;

    public RecommenderQualityReportTests(RecommenderEvaluationHarness harness, ITestOutputHelper output)
    {
        _harness = harness;
        _output = output;
    }

    [PostgresFact]
    public void Relatorio_dos_proxies_com_a_configuracao_que_produziu_cada_numero()
    {
        var evaluator = new RecommenderQualityEvaluator();
        var report = new StringBuilder();

        long startTimestamp = Stopwatch.GetTimestamp();

        AppendCensus(report);
        AppendCoherenceTable(report, evaluator);
        AppendDuplicateTable(report, evaluator);

        TimeSpan evaluationDuration = Stopwatch.GetElapsedTime(startTimestamp);
        AppendCost(report, evaluationDuration);

        _output.WriteLine(report.ToString());
    }

    private void AppendCensus(StringBuilder report)
    {
        report.AppendLine(CultureInfo.InvariantCulture,
            $"CENSO: {_harness.Census.EligibleTracks} faixas elegiveis, {_harness.Census.ImputedTracks} imputadas.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"DUPLICATAS: {_harness.Census.DuplicateGroups} grupos, {_harness.Census.DuplicatedTracks} faixas, " +
            $"{_harness.Census.DuplicatePairs} pares, maior grupo {_harness.Census.LargestGroup}.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"AMOSTRA (seed '{RecommenderEvaluationHarness.SamplingSeed}'): " +
            $"{_harness.Sample.SeedTrackIds.Count} sementes, top-N {RecommenderEvaluationHarness.TopN}; " +
            $"grupos: {_harness.Sample.DuplicateGroups.Count} com teto de " +
            $"{RecommenderEvaluationHarness.MaxMembersPerGroup}, " +
            $"{_harness.UncappedDuplicateSample.DuplicateGroups.Count} sem teto, " +
            $"{_harness.LargeDuplicateGroupSample.DuplicateGroups.Count} grupos de " +
            $"{RecommenderEvaluationHarness.LargeGroupMinimumMembers}+ faixas.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"COBERTURA COLABORATIVA: {_harness.SeedsWithCollaborativeCoverage} de " +
            $"{_harness.Sample.SeedTrackIds.Count} sementes tem co-ocorrencia registrada.");
        report.AppendLine();
    }

    /// <summary>
    /// Proxies 1 e 2. A varredura de pesos roda no ranking CRU (pareada com o E4.4) e, logo abaixo, as linhas do
    /// default de HOJE — dedup ligado — e do blend sobre o ranking final, nos dois pesos.
    /// </summary>
    private void AppendCoherenceTable(StringBuilder report, RecommenderQualityEvaluator evaluator)
    {
        var settings = new List<RecommenderEvaluationSetting> { RecommenderEvaluationSetting.CosineOnly() };
        foreach (double weight in SweptWeights)
            settings.Add(RecommenderEvaluationSetting.BoostedBy(weight));

        RecommenderEvaluationSetting productionDefault = RecommenderEvaluationSetting
            .BoostedBy(GenreAffinityPolicy.DefaultBoostWeight)
            .WithDedupe();

        settings.Add(RecommenderEvaluationSetting.CosineOnly().WithDedupe());
        settings.Add(productionDefault);

        foreach (double blendWeight in BlendWeights)
            settings.Add(productionDefault.WithBlend(blendWeight));

        report.AppendLine(CultureInfo.InvariantCulture,
            $"PROXIES 1 e 2 — coerencia de genero e autoexclusao ({_harness.Sample.SeedTrackIds.Count} sementes, " +
            $"top-{RecommenderEvaluationHarness.TopN})");
        report.AppendLine(CoherenceHeader);

        foreach (RecommenderEvaluationSetting setting in settings)
            AppendCoherenceRow(report, evaluator, _harness.Sample, setting);

        report.AppendLine();

        // O blend só age nas sementes COM co-ocorrencia; na amostra inteira ele fica diluido pelo fallback.
        report.AppendLine(CultureInfo.InvariantCulture,
            $"MESMOS PROXIES, so nas {_harness.CollaborativeCoveredSample.SeedTrackIds.Count} sementes com cobertura colaborativa — o unico recorte em que o blend muda o ranking");
        report.AppendLine(CoherenceHeader);

        RecommenderEvaluationSetting coveredDefault = RecommenderEvaluationSetting
            .BoostedBy(GenreAffinityPolicy.DefaultBoostWeight)
            .WithDedupe();

        AppendCoherenceRow(report, evaluator, _harness.CollaborativeCoveredSample, coveredDefault);
        foreach (double blendWeight in BlendWeights)
            AppendCoherenceRow(report, evaluator, _harness.CollaborativeCoveredSample, coveredDefault.WithBlend(blendWeight));

        report.AppendLine();
    }

    private void AppendCoherenceRow(
        StringBuilder report,
        RecommenderQualityEvaluator evaluator,
        RecommenderEvaluationSample sample,
        RecommenderEvaluationSetting setting)
    {
        RecommenderQualityMeasurement measurement = evaluator.Measure(
            _harness.Index, sample, setting, RecommenderEvaluationHarness.TopN, _harness.Context);

        GenreCoherenceProxy coherence = measurement.GenreCoherence;

        report.AppendLine(CultureInfo.InvariantCulture,
            $"{setting.Label,-38} | {coherence.MeanCoherence,9:0.0000} | {coherence.CoherenceStandardDeviation,7:0.0000} | " +
            $"{coherence.CoherenceStandardError,12:0.0000} | {coherence.SaturationRate,9:0.0000} | " +
            $"{coherence.SeedsEvaluated,8} | {coherence.SeedsWithoutUsableGenre,10} | " +
            $"{coherence.SeedsMissingFromIndex,11} | {coherence.ImputedSeeds,9} | " +
            $"{measurement.SelfExclusion.Violations,13}");
    }

    /// <summary>
    /// Proxy 3, nas três amostras e nas duas pontas do dedup. A amostra com teto de 8 reproduz o E4.4; a sem teto
    /// mostra o que o teto escondia; a de grupos grandes é a única em que "top-K 100% duplicado" pode ser diferente
    /// de zero, e é a que responde se o dedup entrega o que prometeu.
    /// </summary>
    private void AppendDuplicateTable(StringBuilder report, RecommenderQualityEvaluator evaluator)
    {
        RecommenderEvaluationSetting withoutDedupe = RecommenderEvaluationSetting.CosineOnly();
        RecommenderEvaluationSetting withDedupe = withoutDedupe.WithDedupe();

        (string Label, RecommenderEvaluationSample Sample, RecommenderEvaluationSetting Setting)[] runs =
        [
            ($"teto {RecommenderEvaluationHarness.MaxMembersPerGroup} (E4.4)", _harness.Sample, withoutDedupe),
            ("sem teto, mesmos grupos", _harness.UncappedDuplicateSample, withoutDedupe),
            ("sem teto, mesmos grupos", _harness.UncappedDuplicateSample, withDedupe),
            ($"grupos {RecommenderEvaluationHarness.LargeGroupMinimumMembers}+", _harness.LargeDuplicateGroupSample, withoutDedupe),
            ($"grupos {RecommenderEvaluationHarness.LargeGroupMinimumMembers}+", _harness.LargeDuplicateGroupSample, withDedupe)
        ];

        report.AppendLine("PROXY 3 — proximidade de duplicatas (genero SEMPRE off; top-K 10)");
        report.AppendLine(DuplicateHeader);

        foreach ((string label, RecommenderEvaluationSample sample, RecommenderEvaluationSetting setting) in runs)
        {
            DuplicateProximityProxy duplicates = evaluator.MeasureDuplicateProximity(
                _harness.Index, sample, RecommenderEvaluationHarness.TopN, setting, _harness.Context);

            report.AppendLine(CultureInfo.InvariantCulture,
                $"{label,-26} | {duplicates.Setting.Label,-31} | {duplicates.GroupsEvaluated,6} | " +
                $"{duplicates.SeedsEvaluated,8} | {duplicates.MeanRecall,6:0.0000} | {duplicates.HitRate,8:0.0000} | " +
                $"{duplicates.MeanFirstSiblingRank,8:0.00} | {duplicates.MeanSiblingCosine,10:0.000000} | " +
                $"{duplicates.SeedsWithFullyDuplicatedTopK,13} | {duplicates.SeedsWithRedundantSiblings,17} | " +
                $"{duplicates.SeedsWithIncompleteTopK,15}");
        }

        report.AppendLine();
    }

    private void AppendCost(StringBuilder report, TimeSpan evaluationDuration)
    {
        report.AppendLine(CultureInfo.InvariantCulture,
            $"CUSTO: indice montado em {_harness.IndexBuildDuration.TotalSeconds:0.00} s " +
            $"({_harness.IndexFootprintMb:0.0} MB retidos), insumos de dedup/blend " +
            $"{_harness.ContextFootprintMb:0.0} MB retidos, avaliacao em {evaluationDuration.TotalSeconds:0.00} s, " +
            $"pico de working set {_harness.PeakWorkingSetMb:0.0} MB.");
    }
}
