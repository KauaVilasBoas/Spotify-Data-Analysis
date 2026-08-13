using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;
using Xunit.Abstractions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Evaluation;

/// <summary>
/// O gate rodando de verdade: mede os três proxies sobre o catálogo REAL, no peso de produção
/// (<see cref="GenreAffinityPolicy.DefaultBoostWeight"/>), e reprova a suíte se qualquer um degradar. É este teste
/// que transforma os limiares do E4.4 em defesa contra regressão, e não em número de relatório.
///
/// <para>Repare que a configuração medida é construída A PARTIR da constante de produção: se alguém subir o peso do
/// boost sem refazer a calibração, é o teto de saturação deste gate que acusa.</para>
/// </summary>
[Collection(RecommenderEvaluationCollection.Name)]
public sealed class RecommenderQualityGateSmokeTests
{
    private readonly RecommenderEvaluationHarness _harness;
    private readonly ITestOutputHelper _output;

    public RecommenderQualityGateSmokeTests(RecommenderEvaluationHarness harness, ITestOutputHelper output)
    {
        _harness = harness;
        _output = output;
    }

    [PostgresFact]
    public void Gate_aprova_o_recomendador_sobre_o_catalogo_real()
    {
        var evaluator = new RecommenderQualityEvaluator();

        RecommenderQualityMeasurement cosineOnly = evaluator.Measure(
            _harness.Index,
            _harness.Sample,
            RecommenderEvaluationSetting.CosineOnly(),
            RecommenderEvaluationHarness.TopN);

        RecommenderQualityMeasurement boosted = evaluator.Measure(
            _harness.Index,
            _harness.Sample,
            RecommenderEvaluationSetting.BoostedBy(GenreAffinityPolicy.DefaultBoostWeight),
            RecommenderEvaluationHarness.TopN);

        DuplicateProximityProxy duplicates = evaluator.MeasureDuplicateProximity(
            _harness.Index, _harness.Sample, RecommenderEvaluationHarness.TopN);

        RecommenderQualityVerdict verdict = new RecommenderQualityGate()
            .Evaluate(cosineOnly, boosted, duplicates);

        _output.WriteLine(
            $"coerencia off {cosineOnly.GenreCoherence.MeanCoherence:0.0000} -> " +
            $"boost {GenreAffinityPolicy.DefaultBoostWeight:0.00} {boosted.GenreCoherence.MeanCoherence:0.0000} " +
            $"(saturacao {boosted.GenreCoherence.SaturationRate:0.0000}); " +
            $"autoexclusao {boosted.SelfExclusion.Violations} violacoes; " +
            $"duplicatas recall {duplicates.MeanRecall:0.0000} hit-rate {duplicates.HitRate:0.0000} " +
            $"cosine {duplicates.MeanSiblingCosine:0.000000}.");

        Assert.True(verdict.IsApproved, string.Join(" | ", verdict.Failures));
    }
}
