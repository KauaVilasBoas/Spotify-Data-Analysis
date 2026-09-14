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
/// <para><b>E re-medida de novo no E4.9</b>, com o over-fetch adaptativo: os sete limiares anteriores continuam
/// passando sem nenhum ajuste de régua, e o que mudou na fixture foram os valores MEDIDOS que o adaptativo move
/// (completude do resultado, e o desvio da coerência na quarta casa). Ajustar limiar para acomodar mudança seria
/// regressão disfarçada de calibração.</para>
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
        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), MeasuredDedupedDuplicates(), MeasuredResultSize());

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

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), boosted, MeasuredDuplicates(), MeasuredDedupedDuplicates(), MeasuredResultSize());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("autoexclusão", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_coerencia_abaixo_do_piso()
    {
        RecommenderQualityMeasurement boosted = WithCoherence(MeasuredBoosted(), meanCoherence: 0.30, saturation: 0.10);

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), boosted, MeasuredDuplicates(), MeasuredDedupedDuplicates(), MeasuredResultSize());

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

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), boosted, MeasuredDuplicates(), MeasuredDedupedDuplicates(), MeasuredResultSize());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("saturação", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_boost_decorativo_que_nao_ganha_do_cosine_puro()
    {
        RecommenderQualityMeasurement boosted = WithCoherence(MeasuredBoosted(), meanCoherence: 0.45, saturation: 0.05);
        RecommenderQualityMeasurement cosineOnly = WithCoherence(MeasuredCosineOnly(), meanCoherence: 0.44, saturation: 0.05);

        RecommenderQualityVerdict verdict = _gate.Evaluate(cosineOnly, boosted, MeasuredDuplicates(), MeasuredDedupedDuplicates(), MeasuredResultSize());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("ganho", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_duplicatas_que_deixaram_de_se_reencontrar()
    {
        DuplicateProximityProxy duplicates = MeasuredDuplicates() with { MeanRecall = 0.20, HitRate = 0.25 };

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), duplicates, MeasuredDedupedDuplicates(), MeasuredResultSize());

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.Failures, failure => failure.Contains("recall de duplicatas", StringComparison.Ordinal));
        Assert.Contains(verdict.Failures, failure => failure.Contains("alcance de duplicatas", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprova_queda_do_cosseno_das_duplicatas_que_denuncia_quebra_na_normalizacao()
    {
        DuplicateProximityProxy duplicates = MeasuredDuplicates() with { MeanSiblingCosine = 0.81 };

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), duplicates, MeasuredDedupedDuplicates(), MeasuredResultSize());

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
            MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), deduped, MeasuredResultSize());

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
            () => _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), trocada, MeasuredDedupedDuplicates(), MeasuredResultSize()));
    }

    [Fact]
    public void Recusa_o_indicador_de_repeticao_medido_sem_dedup()
    {
        DuplicateProximityProxy semDedup = MeasuredDedupedDuplicates() with
        {
            Setting = RecommenderEvaluationSetting.CosineOnly()
        };

        Assert.Throws<DomainException>(
            () => _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), semDedup, MeasuredResultSize()));
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
                MeasuredCosineOnly(), blendada, MeasuredDuplicates(), MeasuredDedupedDuplicates(), MeasuredResultSize()));
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
            () => _gate.Evaluate(cruo, MeasuredBoosted(), MeasuredDuplicates(), MeasuredDedupedDuplicates(), MeasuredResultSize()));
    }

    /// <summary>
    /// O limiar que o E4.9 acrescentou: a lista curta deixa de ser buraco documentado e passa a reprovar o build.
    /// O valor usado aqui é o BASELINE do card (679 de 1.895, 35,83%) — se o over-fetch voltar a ser fixo, é este
    /// teste que traduz a regressão em número.
    /// </summary>
    [Fact]
    public void Reprova_a_distribuicao_de_resultado_curto_do_over_fetch_fixo()
    {
        RecommendationSizeProxy resultSize = MeasuredResultSize() with
        {
            SeedsBelowTopN = 679,
            SeedsWithSingleResult = 227,
            MeanResultSize = 7.87
        };

        RecommenderQualityVerdict verdict = _gate.Evaluate(
            MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), MeasuredDedupedDuplicates(), resultSize);

        Assert.False(verdict.IsApproved);
        Assert.Contains(
            verdict.Failures, failure => failure.Contains("abaixo do top-N pedido", StringComparison.Ordinal));
        Assert.Contains(
            verdict.Failures, failure => failure.Contains("uma única recomendação", StringComparison.Ordinal));
    }

    /// <summary>
    /// Os dois limiares do E4.9 são independentes: 4,9% de listas curtas passa no teto de 5%, mas UMA semente com um
    /// item só ainda reprova. Sem o segundo limiar, 227 listas de um item caberiam folgadas dentro de uma taxa
    /// agregada bonita.
    /// </summary>
    [Fact]
    public void Reprova_semente_com_um_unico_item_mesmo_dentro_do_teto_agregado()
    {
        RecommendationSizeProxy resultSize = MeasuredResultSize() with
        {
            SeedsBelowTopN = 92,
            SeedsWithSingleResult = 1
        };

        RecommenderQualityVerdict verdict = _gate.Evaluate(
            MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), MeasuredDedupedDuplicates(), resultSize);

        Assert.True(
            resultSize.ShortResultRate <= RecommenderQualityGate.MaximumShortResultRate,
            "A premissa do cenário é a taxa agregada estar DENTRO do teto.");
        Assert.False(verdict.IsApproved);
        Assert.Contains(
            verdict.Failures, failure => failure.Contains("uma única recomendação", StringComparison.Ordinal));
    }

    /// <summary>
    /// A completude é gateada sobre a configuração que o endpoint entrega. Medir a distribuição em <c>off</c> e cobrar
    /// o limiar ao lado da coerência medida em <c>boost</c> seria aprovar duas grandezas que ninguém comparou — a
    /// mesma classe de erro que o E4.10 pagou para corrigir. É exceção, não veredito.
    /// </summary>
    [Fact]
    public void Recusa_a_distribuicao_medida_em_outra_configuracao()
    {
        RecommendationSizeProxy outraConfig = MeasuredResultSize() with
        {
            Setting = RecommenderEvaluationSetting.CosineOnly().WithDedupe()
        };

        Assert.Throws<DomainException>(
            () => _gate.Evaluate(
                MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), MeasuredDedupedDuplicates(),
                outraConfig));
    }

    [Fact]
    public void Recusa_a_distribuicao_medida_em_outro_top_n()
    {
        RecommendationSizeProxy outroTopN = MeasuredResultSize() with { TopN = 20 };

        Assert.Throws<DomainException>(
            () => _gate.Evaluate(
                MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), MeasuredDedupedDuplicates(),
                outroTopN));
    }

    // --- E4.12: a guarda do proxy 4 cobrava o eixo que já coincidia, e não a AMOSTRA ---

    /// <summary>
    /// O cenário de falha que a guarda anterior aprovava, e é o mais severo da revisão: alguém "simplifica" a avaliação
    /// passando a amostra SORTEADA às duas medições. Configuração e top-N continuam idênticos — são os dois únicos
    /// eixos que a guarda do E4.9 comparava —, então ela aprovava; e como lista curta é fenômeno dos grupos de 11+
    /// (0,3583 antes do over-fetch adaptativo, contra ~0 numa amostra sorteada), o teto de 5% passava por vacuidade.
    /// Consequência: <b>reverter o over-fetch adaptativo deixava o gate verde.</b>
    ///
    /// <para>Repare que os DOIS valores medidos aqui são os aprovados (0 e 0) e a configuração é a MESMA da medição de
    /// coerência: o que reprova é exclusivamente a população. A guarda tem de ser ABSOLUTA — cobrar apenas que as duas
    /// medições coincidam entre si aprovaria exatamente este cenário.</para>
    /// </summary>
    [Fact]
    public void Recusa_a_distribuicao_medida_na_amostra_sorteada_em_vez_da_populacao_que_calibrou_o_limiar()
    {
        RecommendationSizeProxy naAmostraSorteada = MeasuredResultSize() with
        {
            SeedPopulation = RecommenderEvaluationPopulation.RandomCatalogSeeds,
            SeedsEvaluated = 300
        };

        Assert.Equal(MeasuredBoosted().Setting, naAmostraSorteada.Setting);
        Assert.Equal(MeasuredBoosted().TopN, naAmostraSorteada.TopN);
        Assert.Equal(MeasuredBoosted().SeedPopulation, naAmostraSorteada.SeedPopulation);

        DomainException exception = Assert.Throws<DomainException>(
            () => _gate.Evaluate(
                MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), MeasuredDedupedDuplicates(),
                naAmostraSorteada));

        Assert.Contains("vacuidade", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// O fail-safe: amostra que não declara a própria população não satisfaz a guarda. O default do enum é
    /// <c>Unspecified</c> justamente para que o esquecimento reprove, em vez de o gate herdar por omissão a população
    /// que calibrou o limiar.
    /// </summary>
    [Fact]
    public void Recusa_a_distribuicao_de_uma_amostra_que_nao_declarou_populacao()
    {
        RecommendationSizeProxy semPopulacao = MeasuredResultSize() with
        {
            SeedPopulation = RecommenderEvaluationPopulation.Unspecified
        };

        Assert.Throws<DomainException>(
            () => _gate.Evaluate(
                MeasuredCosineOnly(), MeasuredBoosted(), MeasuredDuplicates(), MeasuredDedupedDuplicates(),
                semPopulacao));
    }

    /// <summary>
    /// O ganho da coerência é uma razão entre duas médias: tirar o numerador das sementes com cobertura colaborativa e
    /// o denominador da amostra inteira produz um número que não descreve sistema nenhum — e o gate o aprovava, porque
    /// só o <c>dedupe</c> era cobrado como pareamento.
    /// </summary>
    [Fact]
    public void Recusa_comparar_ganho_entre_populacoes_diferentes()
    {
        RecommenderQualityMeasurement outraPopulacao = MeasuredBoosted() with
        {
            SeedPopulation = RecommenderEvaluationPopulation.CollaborativeCoveredSeeds
        };

        Assert.Equal(MeasuredCosineOnly().Setting.Dedupe, outraPopulacao.Setting.Dedupe);

        Assert.Throws<DomainException>(
            () => _gate.Evaluate(
                MeasuredCosineOnly(), outraPopulacao, MeasuredDuplicates(), MeasuredDedupedDuplicates(),
                MeasuredResultSize()));
    }

    /// <summary>
    /// O mesmo fail-safe do lado da coerência, e ele é necessário: duas medições <c>Unspecified</c> COINCIDEM, então
    /// uma guarda de igualdade pura seria satisfeita por duas medições sem identidade nenhuma.
    /// </summary>
    [Fact]
    public void Recusa_ganho_entre_duas_medicoes_sem_populacao_declarada()
    {
        RecommenderQualityMeasurement cosineSemPopulacao = MeasuredCosineOnly() with
        {
            SeedPopulation = RecommenderEvaluationPopulation.Unspecified
        };

        RecommenderQualityMeasurement boostedSemPopulacao = MeasuredBoosted() with
        {
            SeedPopulation = RecommenderEvaluationPopulation.Unspecified
        };

        Assert.Equal(cosineSemPopulacao.SeedPopulation, boostedSemPopulacao.SeedPopulation);

        Assert.Throws<DomainException>(
            () => _gate.Evaluate(
                cosineSemPopulacao, boostedSemPopulacao, MeasuredDuplicates(), MeasuredDedupedDuplicates(),
                MeasuredResultSize()));
    }

    [Fact]
    public void Falhas_trazem_o_valor_medido_e_o_limiar_para_o_diagnostico_nao_exigir_arqueologia()
    {
        DuplicateProximityProxy duplicates = MeasuredDuplicates() with { MeanRecall = 0.20 };

        RecommenderQualityVerdict verdict = _gate.Evaluate(MeasuredCosineOnly(), MeasuredBoosted(), duplicates, MeasuredDedupedDuplicates(), MeasuredResultSize());

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
            RecommenderEvaluationPopulation.RandomCatalogSeeds,
            TopN: 10,
            new GenreCoherenceProxy(
                SeedsEvaluated: 300, SeedsWithoutUsableGenre: 0, SeedsWithEmptyTopN: 0, SeedsMissingFromIndex: 0,
                ImputedSeeds: 0, MeanCoherence: 0.1140, SaturatedSeeds: 2, MeanImputedNeighbors: 0,
                CoherenceStandardDeviation: 0.1734),
            new SelfExclusionProxy(300, 0));

    /// <summary>
    /// Boost no peso de produção, configuração default do endpoint, re-medido em 2026-09-13 com o over-fetch
    /// adaptativo (E4.9): 0,5563 ± 0,3607, saturação 0,2800 (84 de 300). Antes do adaptativo, 0,5563 ± 0,3608 — a
    /// média e a saturação não se moveram, e o desvio mudou na quarta casa porque as sementes que recebiam lista curta
    /// passaram a receber top-10 cheio, alterando o denominador da coerência delas. O E4.4 registrava 0,5833 e 0,3100
    /// sem dedup.
    /// </summary>
    private static RecommenderQualityMeasurement MeasuredBoosted() =>
        new(
            RecommenderEvaluationSetting.BoostedBy(0.05).WithDedupe(),
            RecommenderEvaluationPopulation.RandomCatalogSeeds,
            TopN: 10,
            new GenreCoherenceProxy(
                SeedsEvaluated: 300, SeedsWithoutUsableGenre: 0, SeedsWithEmptyTopN: 0, SeedsMissingFromIndex: 0,
                ImputedSeeds: 0, MeanCoherence: 0.5563, SaturatedSeeds: 84, MeanImputedNeighbors: 0,
                CoherenceStandardDeviation: 0.3607),
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
    /// Proxy 3 com o dedup LIGADO sobre os 111 grupos de 11+ faixas, re-medido em 2026-09-13 com o over-fetch
    /// adaptativo (E4.9): a repetição no top-N continua zero (era 1.768 sem dedup) e as DUAS sobras do E4.8 zeraram —
    /// <c>SeedsWithIncompleteTopK</c> de 548 para 0 e os 186 top-K "100% duplicados" para 0.
    ///
    /// <para>Os dois zeram pela mesma causa e não por coincidência: um top-K "100% duplicado" era, medido, uma lista
    /// de 1 ou 2 itens em que o único item era irmão da semente. Com o top-10 completo, o irmão divide a lista com
    /// oito faixas distintas — e é por isso que o recall (0,0958) NÃO sobe: o numerador é o mesmo, o K é o mesmo, o
    /// que mudou é a lista deixar de ser curta.</para>
    /// </summary>
    private static DuplicateProximityProxy MeasuredDedupedDuplicates() =>
        new(
            GroupsEvaluated: 111,
            SeedsEvaluated: 1895,
            MeanRecall: 0.0958,
            HitRate: 0.9583,
            MeanFirstSiblingRank: 1.04,
            MeanSiblingCosine: 0.997834,
            SeedsWithFullyDuplicatedTopK: 0,
            SeedsWithRedundantSiblings: 0,
            SeedsWithIncompleteTopK: 0,
            RecommenderEvaluationSetting.CosineOnly().WithDedupe());

    /// <summary>
    /// Proxy 4 — a distribuição de tamanho do resultado, medida em 2026-09-13 na configuração default do endpoint
    /// (<c>boost 0.050 | dedupe=on | content</c>) sobre as MESMAS 1.895 sementes dos grupos de 11+ faixas:
    /// <b>0 abaixo de 10</b> (eram 679) e <b>0 com exatamente 1</b> (eram 227), tamanho médio 10,00 (era 7,87).
    ///
    /// <para>O custo, medido na mesma passada: 1.216 sementes resolvem na primeira rodada, 562 precisam da segunda e
    /// 117 da terceira. Nenhuma bate no teto sem resolver.</para>
    ///
    /// <para><b>A população é parte do número</b> (E4.12): estes 1.895 ids são os membros dos grupos de 11+, e é sobre
    /// ELES que o teto de 5% foi calibrado. Trocar a população por uma amostra sorteada manteria configuração e top-N
    /// idênticos e mediria um fenômeno que lá não acontece.</para>
    /// </summary>
    private static RecommendationSizeProxy MeasuredResultSize() =>
        new(
            SeedsEvaluated: 1895,
            SeedsMissingFromIndex: 0,
            SeedsBelowTopN: 0,
            SeedsWithSingleResult: 0,
            SeedsWithEmptyResult: 0,
            MeanResultSize: 10.00,
            SeedsByRound: [1216, 562, 117],
            RecommenderEvaluationSetting.BoostedBy(0.05).WithDedupe(),
            RecommenderEvaluationPopulation.LargeNearDuplicateGroupMembers,
            TopN: 10);

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
