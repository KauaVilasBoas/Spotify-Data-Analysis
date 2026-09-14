using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;
using Xunit.Abstractions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Evaluation;

/// <summary>
/// O gate rodando de verdade: mede os proxies sobre o catálogo REAL, na CONFIGURAÇÃO DEFAULT DO ENDPOINT
/// (<c>genreMode=boost</c> no peso de produção, <c>dedupe=true</c>, <c>strategy=content</c>), e reprova a suíte se
/// qualquer um degradar. É este teste que transforma os limiares em defesa contra regressão, e não em número de
/// relatório.
///
/// <para><b>O que o E4.8 corrigiu aqui:</b> até então o smoke media o ranking CRU, sem o dedup que é default desde
/// o E4.7 — ou seja, defendia um sistema que o endpoint já não entregava. Agora a configuração medida é construída
/// a partir das constantes de produção (<see cref="GenreAffinityPolicy.DefaultBoostWeight"/> e o dedup ligado): se
/// alguém subir o peso do boost sem refazer a calibração, é o teto de saturação deste gate que acusa.</para>
///
/// <para><b>O proxy 3 é medido duas vezes, de propósito (DP-2).</b> Com <c>dedupe=false</c> ele mede o MOTOR de
/// similaridade — o instrumento exige as duplicatas visíveis, e é esse número que os limiares de recall/alcance/
/// cosseno cobram. Com <c>dedupe=true</c> ele responde outra pergunta: o top-N que o usuário recebe ainda repete a
/// mesma obra? São duas perguntas, e por isso dois números.</para>
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
    public void Gate_aprova_o_recomendador_na_configuracao_default_do_endpoint()
    {
        var evaluator = new RecommenderQualityEvaluator();

        // O dedup é default do endpoint (E4.7), então as DUAS medições de coerência o incluem — a comparação de
        // ganho só é válida pareada, e comparar um lado deduplicado com outro cru mediria o dedup, não o boost.
        RecommenderEvaluationSetting cosineOnlySetting = RecommenderEvaluationSetting.CosineOnly().WithDedupe();
        RecommenderEvaluationSetting boostedSetting = RecommenderEvaluationSetting
            .BoostedBy(GenreAffinityPolicy.DefaultBoostWeight)
            .WithDedupe();

        RecommenderQualityMeasurement cosineOnly = evaluator.Measure(
            _harness.Index, _harness.Sample, cosineOnlySetting, RecommenderEvaluationHarness.TopN, _harness.Context);

        RecommenderQualityMeasurement boosted = evaluator.Measure(
            _harness.Index, _harness.Sample, boostedSetting, RecommenderEvaluationHarness.TopN, _harness.Context);

        // Proxy 3 sem o teto de 8 membros por grupo: o teto tornava "top-K 100% duplicado" impossível por
        // aritmética, e o E4.4 registrou que a dor real está nos grupos grandes.
        DuplicateProximityProxy similarityEngineDuplicates = evaluator.MeasureDuplicateProximity(
            _harness.Index,
            _harness.UncappedDuplicateSample,
            RecommenderEvaluationHarness.TopN,
            RecommenderEvaluationSetting.CosineOnly(),
            _harness.Context);

        // A cura, medida onde a dor existe: os grupos de 11+ faixas, com o dedup ligado.
        DuplicateProximityProxy dedupedDuplicates = evaluator.MeasureDuplicateProximity(
            _harness.Index,
            _harness.LargeDuplicateGroupSample,
            RecommenderEvaluationHarness.TopN,
            RecommenderEvaluationSetting.CosineOnly().WithDedupe(),
            _harness.Context);

        // Proxy 4 (E4.9): a distribuição de tamanho do resultado, na MESMA configuração cuja qualidade é gateada, e
        // sobre as 1.895 sementes dos grupos de 11+ — a população em que o defeito do over-fetch fixo foi medido e em
        // que os dois limiares foram calibrados. Desde o E4.12 o gate COBRA essa população: passar aqui a amostra
        // sorteada deixaria configuração e top-N idênticos e mediria um fenômeno que lá não acontece.
        Assert.Equal(
            RecommenderQualityGate.CalibrationPopulationOfResultSize,
            _harness.LargeGroupMemberSample.SeedPopulation);

        RecommendationSizeProxy resultSize = RecommenderQualityEvaluator.MeasureResultSize(
            _harness.Index,
            _harness.LargeGroupMemberSample,
            boostedSetting,
            RecommenderEvaluationHarness.TopN,
            _harness.Context);

        RecommenderQualityVerdict verdict = new RecommenderQualityGate()
            .Evaluate(cosineOnly, boosted, similarityEngineDuplicates, dedupedDuplicates, resultSize);

        _output.WriteLine(
            $"[{resultSize.Setting.Label}] [{resultSize.SeedPopulation}] tamanho do resultado: " +
            $"{resultSize.SeedsBelowTopN} de " +
            $"{resultSize.SeedsEvaluated} sementes abaixo de {resultSize.TopN} " +
            $"({resultSize.ShortResultRate:0.0000}), {resultSize.SeedsWithSingleResult} com um item so; " +
            $"rodadas de over-fetch: {string.Join(" / ", resultSize.SeedsByRound)}.");

        _output.WriteLine(
            $"[{boosted.Setting.Label}] [{boosted.SeedPopulation}] " +
            $"coerencia {cosineOnly.GenreCoherence.MeanCoherence:0.0000} -> " +
            $"{boosted.GenreCoherence.MeanCoherence:0.0000} " +
            $"(saturacao {boosted.GenreCoherence.SaturationRate:0.0000}); " +
            $"autoexclusao {boosted.SelfExclusion.Violations} violacoes; " +
            $"[{similarityEngineDuplicates.Setting.Label}] duplicatas recall " +
            $"{similarityEngineDuplicates.MeanRecall:0.0000} hit-rate {similarityEngineDuplicates.HitRate:0.0000} " +
            $"cosine {similarityEngineDuplicates.MeanSiblingCosine:0.000000}; " +
            $"[{dedupedDuplicates.Setting.Label}] repeticao no top-N em " +
            $"{dedupedDuplicates.SeedsWithRedundantSiblings} de {dedupedDuplicates.SeedsEvaluated} sementes, " +
            $"top-K 100% duplicado em {dedupedDuplicates.SeedsWithFullyDuplicatedTopK}.");

        Assert.True(verdict.IsApproved, string.Join(" | ", verdict.Failures));
    }
}
