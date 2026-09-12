using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Evaluation;

/// <summary>
/// O gate precisa reprovar pelos dois lados. Estes testes partem dos números REAIS medidos no catálogo (a linha de
/// base aprovada) e degradam um proxy de cada vez — inclusive para CIMA, no caso da saturação, que é a falha que um
/// gate ingênuo de piso deixaria passar.
///
/// <para><b>A linha de base foi re-medida no E4.8</b>, na configuração default do endpoint de HOJE
/// (<c>dedupe=true</c>): os números do E4.4 descreviam o ranking cru, que deixou de ser o que o usuário recebe.
/// Nenhum limiar foi afrouxado — todos continuam passando —, mas a fixture agora reflete o sistema real.</para>
///
/// <para>Os testes de recusa (<c>Recusa_*</c>) cobrem a outra metade do trabalho do gate: um gate alimentado com a
/// configuração errada não erra o número, erra a PERGUNTA — e devolveria um "aprovado" confiante sobre outra
/// grandeza. Isso é exceção, não veredito.</para>
/// </summary>
public sealed class RecommenderQualityGateTests
{
    private readonly RecommenderQualityGate _gate = new();

    [Fact]
    public void Aprova_a_linha_de_base_medida_no_catalogo_real()
    {
        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), MeasuredDedupedDuplicates());

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

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), boosted, MeasuredDuplicates(), MeasuredDedupedDuplicates());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("autoexclusão", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_coerencia_abaixo_do_piso()
    {
        RecommenderQualityMeasurement boosted = WithCoherence(MeasuredBoosted(), meanCoherence: 0.30, saturation: 0.10);

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), boosted, MeasuredDuplicates(), MeasuredDedupedDuplicates());

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

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), boosted, MeasuredDuplicates(), MeasuredDedupedDuplicates());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("saturação", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_boost_decorativo_que_nao_ganha_do_cosine_puro()
    {
        RecommenderQualityMeasurement boosted = WithCoherence(MeasuredBoosted(), meanCoherence: 0.45, saturation: 0.05);
        RecommenderQualityMeasurement cosineOnly = WithCoherence(MeasuredCosineOnly(), meanCoherence: 0.44, saturation: 0.05);

        RecommenderQualityVerdict verdict = _gate.Evaluate(cosineOnly, boosted, MeasuredDuplicates(), MeasuredDedupedDuplicates());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("ganho", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_duplicatas_que_deixaram_de_se_reencontrar()
    {
        DuplicateProximityProxy duplicates = MeasuredDuplicates() with { MeanRecall = 0.20, HitRate = 0.25 };

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), duplicates, MeasuredDedupedDuplicates());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("recall de duplicatas", StringComparison.Ordinal));
        Assert.Contains(verdict.Failures, failure => failure.Contains("alcance de duplicatas", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_queda_do_cosseno_das_duplicatas_que_denuncia_quebra_na_normalizacao()
    {
        DuplicateProximityProxy duplicates = MeasuredDuplicates() with { MeanSiblingCosine = 0.81 };

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), duplicates, MeasuredDedupedDuplicates());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("cosseno médio", StringComparison.Ordinal));
    }

    /// <summary>
    /// O indicador que o E4.8 acrescentou (DP-2): o dedup existe para que a mesma obra não ocupe duas posições do
    /// top-N. Se sobrar repetição depois de colapsar, as duas normalizações de "artista|título" — a do Catalog e a
    /// reconstruída no Prediction — divergiram, e é aqui que isso vira build vermelho.
    /// </summary>
    [Fact]
    public void Reprova_repeticao_da_mesma_obra_que_sobreviveu_ao_dedup()
    {
        DuplicateProximityProxy deduped = MeasuredDedupedDuplicates() with { SeedsWithRedundantSiblings = 1 };

        RecommenderQualityVerdict verdict = _gate.Evaluate(
            MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), deduped);

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("repetição no top-N", StringComparison.Ordinal));
    }

    /// <summary>
    /// Recall, alcance e cosseno só significam alguma coisa sobre o ranking com as duplicatas VISÍVEIS. Passar a
    /// medição deduplicada nesse lugar cobraria o limiar de ontem sobre outra grandeza — e produziria um "aprovado"
    /// confiante e errado, que é pior que gate nenhum. Por isso é exceção, não reprovação silenciosa.
    /// </summary>
    [Fact]
    public void Recusa_medir_o_motor_de_similaridade_sobre_um_ranking_ja_deduplicado()
    {
        DuplicateProximityProxy trocada = MeasuredDuplicates() with
        {
            Setting = RecommenderEvaluationSetting.CosineOnly().WithDedupe()
        };

        Assert.Throws<DomainException>(
            () => _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), trocada, MeasuredDedupedDuplicates()));
    }

    [Fact]
    public void Recusa_o_indicador_de_repeticao_medido_sem_dedup()
    {
        DuplicateProximityProxy semDedup = MeasuredDedupedDuplicates() with
        {
            Setting = RecommenderEvaluationSetting.CosineOnly()
        };

        Assert.Throws<DomainException>(
            () => _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), semDedup));
    }

    /// <summary>DP-1 do E4.8: o gate reprova o build só em <c>strategy=content</c>; o blend é medido, não gateado.</summary>
    [Fact]
    public void Recusa_gatear_o_blend_que_ainda_nao_tem_peso_default_validado()
    {
        RecommenderQualityMeasurement blendada = MeasuredBoosted() with
        {
            Setting = RecommenderEvaluationSetting.BoostedBy(0.05).WithDedupe().WithBlend(0.35)
        };

        Assert.Throws<DomainException>(
            () => _gate.Evaluate(
                MeasuredCosineOnly(), blendada, MeasuredDuplicates(), MeasuredDedupedDuplicates()));
    }

    /// <summary>
    /// O ganho é uma razão entre duas médias: comparar um lado deduplicado com outro cru mediria o efeito do dedup
    /// e o creditaria ao boost. O gate recusa a comparação despareada em vez de devolver um número bonito.
    /// </summary>
    [Fact]
    public void Recusa_comparar_ganho_entre_configuracoes_de_dedup_diferentes()
    {
        RecommenderQualityMeasurement cruo = MeasuredCosineOnly() with
        {
            Setting = RecommenderEvaluationSetting.CosineOnly()
        };

        Assert.Throws<DomainException>(
            () => _gate.Evaluate(cruo, MeasuredBoosted(), MeasuredDuplicates(), MeasuredDedupedDuplicates()));
    }

    [Fact]
    public void Falhas_trazem_o_valor_medido_e_o_limiar_para_o_diagnostico_nao_exigir_arqueologia()
    {
        DuplicateProximityProxy duplicates = MeasuredDuplicates() with { MeanRecall = 0.20 };

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), duplicates, MeasuredDedupedDuplicates());

        string failure = Assert.Single(verdict.Failures);
        Assert.Contains("0.2000", failure, StringComparison.Ordinal);
        Assert.Contains("0.4500", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// Cosine puro na configuração DEFAULT do endpoint (dedup ligado), re-medido em 2026-09-12: 0,1140 ± 0,1734
    /// sobre 300 sementes. O E4.4 registrava 0,1347 — sem dedup, porque o dedup ainda não existia.
    /// </summary>
    private static RecommenderQualityMeasurement MeasuredCosineOnly() =>
        new(
            RecommenderEvaluationSetting.CosineOnly().WithDedupe(),
            TopN: 10,
            new GenreCoherenceProxy(
                300, 0, 0, 0, MeanCoherence: 0.1140, SaturatedSeeds: 2, MeanImputedNeighbors: 0,
                CoherenceStandardDeviation: 0.1734),
            new SelfExclusionProxy(300, 0));

    /// <summary>
    /// Boost no peso de produção, configuração default do endpoint, re-medido em 2026-09-12: 0,5563 ± 0,3608,
    /// saturação 0,2800 (84 de 300). O E4.4 registrava 0,5833 e 0,3100 sem dedup.
    /// </summary>
    private static RecommenderQualityMeasurement MeasuredBoosted() =>
        new(
            RecommenderEvaluationSetting.BoostedBy(0.05).WithDedupe(),
            TopN: 10,
            new GenreCoherenceProxy(
                300, 0, 0, 0, MeanCoherence: 0.5563, SaturatedSeeds: 84, MeanImputedNeighbors: 0,
                CoherenceStandardDeviation: 0.3608),
            new SelfExclusionProxy(300, 0));

    /// <summary>
    /// Proxy 3 sobre o MOTOR de similaridade (dedupe=false), 300 grupos sem o teto de 8 membros, re-medido em
    /// 2026-09-12. O E4.4 media os mesmos grupos com teto 8: recall 0,6108, hit-rate 0,6745, cosseno 0,998244.
    /// </summary>
    private static DuplicateProximityProxy MeasuredDuplicates() =>
        new(
            GroupsEvaluated: 300,
            SeedsEvaluated: 908,
            MeanRecall: 0.6464,
            HitRate: 0.7037,
            MeanFirstSiblingRank: 1.26,
            MeanSiblingCosine: 0.999028,
            SeedsWithFullyDuplicatedTopK: 143,
            SeedsWithRedundantSiblings: 361,
            SeedsWithIncompleteTopK: 0,
            RecommenderEvaluationSetting.CosineOnly());

    /// <summary>
    /// Proxy 3 com o dedup LIGADO sobre os 111 grupos de 11+ faixas, re-medido em 2026-09-12: a repetição no top-N
    /// zera (1.768 → 0). Os 186 top-K "100% duplicados" que sobram NÃO são repetição: são listas encurtadas, o
    /// defeito que <c>SeedsWithIncompleteTopK</c> mede e que o gate documenta como buraco conhecido.
    /// </summary>
    private static DuplicateProximityProxy MeasuredDedupedDuplicates() =>
        new(
            GroupsEvaluated: 111,
            SeedsEvaluated: 1895,
            MeanRecall: 0.0958,
            HitRate: 0.9578,
            MeanFirstSiblingRank: 1.04,
            MeanSiblingCosine: 0.998626,
            SeedsWithFullyDuplicatedTopK: 186,
            SeedsWithRedundantSiblings: 0,
            SeedsWithIncompleteTopK: 548,
            RecommenderEvaluationSetting.CosineOnly().WithDedupe());

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
