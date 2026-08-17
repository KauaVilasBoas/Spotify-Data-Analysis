using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Evaluation;

/// <summary>
/// O gate precisa reprovar pelos dois lados. Estes testes partem dos números REAIS medidos no catálogo (a linha de
/// base aprovada) e degradam um proxy de cada vez — inclusive para CIMA, no caso da saturação, que é a falha que um
/// gate ingênuo de piso deixaria passar.
/// </summary>
public sealed class RecommenderQualityGateTests
{
    private readonly RecommenderQualityGate _gate = new();

    [Fact]
    public void Aprova_a_linha_de_base_medida_no_catalogo_real()
    {
        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates());

        Assert.True(verdict.IsApproved);
        Assert.Empty(verdict.Failures);
    }

    [Fact]
    public void Reprova_qualquer_violacao_de_autoexclusao()
    {
        RecommenderQualityMeasurement boosted = MeasuredBoosted() with
        {
            SelfExclusion = new SelfExclusionProxy(SeedsEvaluated: 300, Violations: 1)
        };

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), boosted, MeasuredDuplicates());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("autoexclusão", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_coerencia_abaixo_do_piso()
    {
        RecommenderQualityMeasurement boosted = WithCoherence(MeasuredBoosted(), meanCoherence: 0.30, saturation: 0.10);

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), boosted, MeasuredDuplicates());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("coerência de gênero", StringComparison.Ordinal));
    }

    /// <summary>
    /// O lado que um gate só de piso não cobre: um boost enorme cravaria a coerência em ~1,0 e passaria folgado
    /// num gate ingênuo, embora tivesse virado filtro duro disfarçado — exatamente o defeito que o E4.4 diagnosticou
    /// no peso 0,15. Aqui isso REPROVA.
    /// </summary>
    [Fact]
    public void Reprova_boost_que_saturou_e_virou_filtro_disfarcado()
    {
        RecommenderQualityMeasurement boosted = WithCoherence(MeasuredBoosted(), meanCoherence: 0.9143, saturation: 0.8233);

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), boosted, MeasuredDuplicates());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("saturação", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_boost_decorativo_que_nao_ganha_do_cosine_puro()
    {
        RecommenderQualityMeasurement boosted = WithCoherence(MeasuredBoosted(), meanCoherence: 0.45, saturation: 0.05);
        RecommenderQualityMeasurement cosineOnly = WithCoherence(MeasuredCosineOnly(), meanCoherence: 0.44, saturation: 0.05);

        RecommenderQualityVerdict verdict = _gate.Evaluate(cosineOnly, boosted, MeasuredDuplicates());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("ganho", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_duplicatas_que_deixaram_de_se_reencontrar()
    {
        DuplicateProximityProxy duplicates = MeasuredDuplicates() with { MeanRecall = 0.20, HitRate = 0.25 };

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), duplicates);

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("recall de duplicatas", StringComparison.Ordinal));
        Assert.Contains(verdict.Failures, failure => failure.Contains("alcance de duplicatas", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_queda_do_cosseno_das_duplicatas_que_denuncia_quebra_na_normalizacao()
    {
        DuplicateProximityProxy duplicates = MeasuredDuplicates() with { MeanSiblingCosine = 0.81 };

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), duplicates);

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("cosseno médio", StringComparison.Ordinal));
    }

    [Fact]
    public void Falhas_trazem_o_valor_medido_e_o_limiar_para_o_diagnostico_nao_exigir_arqueologia()
    {
        DuplicateProximityProxy duplicates = MeasuredDuplicates() with { MeanRecall = 0.20 };

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), duplicates);

        string failure = Assert.Single(verdict.Failures);
        Assert.Contains("0.2000", failure, StringComparison.Ordinal);
        Assert.Contains("0.4500", failure, StringComparison.Ordinal);
    }

    private static RecommenderQualityMeasurement MeasuredCosineOnly() =>
        new(
            RecommenderEvaluationSetting.CosineOnly(),
            TopN: 10,
            new GenreCoherenceProxy(300, 0, 0, 0, MeanCoherence: 0.1347, SaturatedSeeds: 3, MeanImputedNeighbors: 0),
            new SelfExclusionProxy(300, 0));

    private static RecommenderQualityMeasurement MeasuredBoosted() =>
        new(
            RecommenderEvaluationSetting.BoostedBy(0.05),
            TopN: 10,
            new GenreCoherenceProxy(300, 0, 0, 0, MeanCoherence: 0.5833, SaturatedSeeds: 93, MeanImputedNeighbors: 0),
            new SelfExclusionProxy(300, 0));

    private static DuplicateProximityProxy MeasuredDuplicates() =>
        new(
            GroupsEvaluated: 300,
            SeedsEvaluated: 808,
            MeanRecall: 0.6108,
            HitRate: 0.6745,
            MeanFirstSiblingRank: 1.31,
            MeanSiblingCosine: 0.998244,
            SeedsWithFullyDuplicatedTopK: 0);

    private static RecommenderQualityMeasurement WithCoherence(
        RecommenderQualityMeasurement measurement, double meanCoherence, double saturation)
    {
        int seeds = measurement.GenreCoherence.SeedsEvaluated;

        return measurement with
        {
            GenreCoherence = measurement.GenreCoherence with
            {
                MeanCoherence = meanCoherence,
                SaturatedSeeds = (int)Math.Round(saturation * seeds)
            }
        };
    }
}
