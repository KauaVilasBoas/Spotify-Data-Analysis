using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Blending;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Deduplication;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations.Evaluation;

/// <summary>
/// Sanidade do INSTRUMENTO de medição do E4.4 sobre índices sintéticos de resposta conhecida. Um proxy que mede
/// errado é pior que proxy nenhum — ele produz um número confiável e falso, e o gate passaria a defender a coisa
/// errada. Aqui cada proxy é confrontado com um cenário cuja resposta é calculável à mão.
/// </summary>
public sealed class RecommenderQualityEvaluatorTests
{
    private readonly RecommenderQualityEvaluator _evaluator = new();

    [Fact]
    public void Coerencia_e_1_quando_todo_o_top_n_compartilha_o_genero_da_semente()
    {
        SimilarityIndex index = BuildIndex(
            Track("seed", 0.80, "pop"),
            Track("p1", 0.79, "pop"),
            Track("p2", 0.78, "pop"));

        RecommenderQualityMeasurement measurement = _evaluator.Measure(
            index, SampleOf("seed"), RecommenderEvaluationSetting.CosineOnly(), topN: 2);

        Assert.Equal(1.0, measurement.GenreCoherence.MeanCoherence, precision: 10);
        Assert.Equal(1, measurement.GenreCoherence.SaturatedSeeds);
        Assert.Equal(1.0, measurement.GenreCoherence.SaturationRate, precision: 10);
    }

    [Fact]
    public void Coerencia_e_a_fracao_do_top_n_do_mesmo_genero()
    {
        SimilarityIndex index = BuildIndex(
            Track("seed", 0.80, "pop"),
            Track("p1", 0.79, "pop"),
            Track("r1", 0.78, "rock"),
            Track("r2", 0.77, "rock"),
            Track("r3", 0.10, "rock"));

        RecommenderQualityMeasurement measurement = _evaluator.Measure(
            index, SampleOf("seed"), RecommenderEvaluationSetting.CosineOnly(), topN: 4);

        Assert.Equal(0.25, measurement.GenreCoherence.MeanCoherence, precision: 10);
        Assert.Equal(0, measurement.GenreCoherence.SaturatedSeeds);
    }

    /// <summary>
    /// O proxy 1 tem de ser comparável entre <c>off</c> e cada peso: ele lê os RÓTULOS dos dois lados, e não quem
    /// recebeu bônus. Se lesse o bônus, daria zero no modo off por construção e a tabela de calibração inteira
    /// perderia o piso contra o qual o ganho é medido.
    /// </summary>
    /// <summary>
    /// O proxy 1 precisa dar o MESMO tipo de leitura no modo off e no modo boost, senão a tabela de calibração fica
    /// sem piso: se ele contasse "quem recebeu bônus", o cosine puro mediria zero por construção e o ganho do boost
    /// seria infinito em qualquer catálogo. Aqui, com o gênero desligado e todos os vizinhos do gênero da semente,
    /// a coerência tem de ser 1 — o que só acontece se ele estiver lendo RÓTULOS.
    /// </summary>
    [Fact]
    public void Coerencia_no_modo_off_le_os_rotulos_e_nao_o_bonus()
    {
        SimilarityIndex index = BuildIndex(
            Track("seed", 0.80, "pop"),
            Track("p1", 0.80, "pop"),
            Track("p2", 0.20, "pop"));

        RecommenderQualityMeasurement measurement = _evaluator.Measure(
            index, SampleOf("seed"), RecommenderEvaluationSetting.CosineOnly(), topN: 2);

        Assert.Equal(1.0, measurement.GenreCoherence.MeanCoherence, precision: 10);
        Assert.Equal(GenreRankingMode.Off, RecommenderEvaluationSetting.CosineOnly().Mode);
    }

    /// <summary>
    /// A configuração da avaliação tem de instanciar a política de PRODUÇÃO do E4.3, e não uma regra paralela —
    /// medir com uma mecânica diferente da que roda no endpoint mediria outro sistema.
    /// </summary>
    [Fact]
    public void Configuracao_de_boost_instancia_a_politica_de_producao_com_o_peso_pedido()
    {
        GenreAffinityPolicy policy = RecommenderEvaluationSetting
            .BoostedBy(0.07)
            .PolicyFor(seedGenre: "pop", seedGenreIsImputed: false);

        Assert.Equal(GenreRankingMode.Boost, policy.Mode);
        Assert.Equal(0.07, policy.BoostWeight, precision: 10);
        Assert.Equal(0.07, policy.GenreBonusFor("pop", candidateGenreIsImputed: false), precision: 10);
        Assert.Equal(0.0, policy.GenreBonusFor("rock", candidateGenreIsImputed: false), precision: 10);
    }

    [Fact]
    public void Semente_sem_genero_nao_entra_na_media_e_e_contada_a_parte()
    {
        SimilarityIndex index = BuildIndex(
            Track("seed", 0.80, genre: null),
            Track("p1", 0.79, "pop"));

        RecommenderQualityMeasurement measurement = _evaluator.Measure(
            index, SampleOf("seed"), RecommenderEvaluationSetting.CosineOnly(), topN: 1);

        Assert.Equal(0, measurement.GenreCoherence.SeedsEvaluated);
        Assert.Equal(1, measurement.GenreCoherence.SeedsWithoutUsableGenre);
        Assert.Equal(0, measurement.GenreCoherence.SeedsMissingFromIndex);
    }

    [Fact]
    public void Semente_fora_do_indice_e_contada_e_nao_silenciada()
    {
        SimilarityIndex index = BuildIndex(Track("a", 0.80, "pop"), Track("b", 0.79, "pop"));

        RecommenderQualityMeasurement measurement = _evaluator.Measure(
            index, SampleOf("inexistente"), RecommenderEvaluationSetting.CosineOnly(), topN: 1);

        Assert.Equal(1, measurement.GenreCoherence.SeedsMissingFromIndex);
        Assert.Equal(0, measurement.SelfExclusion.SeedsEvaluated);
    }

    [Fact]
    public void Semente_imputada_e_contada_explicitamente()
    {
        SimilarityIndex index = BuildIndex(
            Track("seed", 0.80, "pop", isImputed: true),
            Track("p1", 0.79, "pop"));

        RecommenderQualityMeasurement measurement = _evaluator.Measure(
            index, SampleOf("seed"), RecommenderEvaluationSetting.CosineOnly(), topN: 1);

        Assert.Equal(1, measurement.GenreCoherence.ImputedSeeds);
    }

    [Fact]
    public void Autoexclusao_nao_acusa_violacao_no_motor_do_e4_1()
    {
        SimilarityIndex index = BuildIndex(
            Track("seed", 0.80, "pop"),
            Track("p1", 0.79, "pop"),
            Track("p2", 0.78, "pop"));

        RecommenderQualityMeasurement measurement = _evaluator.Measure(
            index, SampleOf("seed", "p1", "p2"), RecommenderEvaluationSetting.CosineOnly(), topN: 5);

        Assert.Equal(3, measurement.SelfExclusion.SeedsEvaluated);
        Assert.Equal(0, measurement.SelfExclusion.Violations);
        Assert.True(measurement.SelfExclusion.IsClean);
    }

    [Fact]
    public void Recall_de_duplicatas_e_1_quando_os_irmaos_ocupam_o_topo()
    {
        SimilarityIndex index = BuildIndex(
            Track("d1", 0.80, "pop"),
            Track("d2", 0.80, "pop"),
            Track("longe", 0.05, "rock"));

        DuplicateProximityProxy duplicates = _evaluator.MeasureDuplicateProximity(
            index, SampleWithGroup("d1", "d2"), topK: 2);

        Assert.Equal(1, duplicates.GroupsEvaluated);
        Assert.Equal(2, duplicates.SeedsEvaluated);
        Assert.Equal(1.0, duplicates.MeanRecall, precision: 10);
        Assert.Equal(1.0, duplicates.HitRate, precision: 10);
        Assert.Equal(1.0, duplicates.MeanFirstSiblingRank, precision: 10);
        Assert.Equal(1.0, duplicates.MeanSiblingCosine, precision: 6);
    }

    /// <summary>
    /// O ponto da DP-4: com um grupo de 4 membros e top-K 2, cada semente só tem COMO reencontrar 2 irmãos. O
    /// denominador é <c>min(n-1, K)</c>, senão o proxy mediria o K e não o recomendador — um recall "0,67" que na
    /// verdade é 2/2 seria lido como defeito inexistente.
    /// </summary>
    [Fact]
    public void Recall_de_duplicatas_e_normalizado_pelo_teto_que_o_top_k_permite()
    {
        SimilarityIndex index = BuildIndex(
            Track("d1", 0.80, "pop"),
            Track("d2", 0.80, "pop"),
            Track("d3", 0.80, "pop"),
            Track("d4", 0.80, "pop"),
            Track("longe", 0.05, "rock"));

        DuplicateProximityProxy duplicates = _evaluator.MeasureDuplicateProximity(
            index, SampleWithGroup("d1", "d2", "d3", "d4"), topK: 2);

        Assert.Equal(1.0, duplicates.MeanRecall, precision: 10);
        Assert.Equal(4, duplicates.SeedsWithFullyDuplicatedTopK);
    }

    /// <summary>
    /// O proxy 3 tem de CAIR quando o espaço deixa de aproximar os irmãos — é essa queda que o gate usa como
    /// detector de quebra na normalização ou no cosseno. Aqui cada membro do par tem dois vizinhos exclusivos mais
    /// próximos que o irmão, então nenhum dos dois reencontra o outro no top-2 e o recall tem de ser exatamente 0.
    /// </summary>
    [Fact]
    public void Recall_de_duplicatas_cai_quando_os_irmaos_nao_se_reencontram()
    {
        SimilarityIndex index = BuildIndex(
            Track("d1", 0.80, "pop"),
            Track("a1", 0.80, "pop"),
            Track("a2", 0.80, "pop"),
            Track("d2", 0.20, "pop"),
            Track("b1", 0.20, "pop"),
            Track("b2", 0.20, "pop"));

        DuplicateProximityProxy duplicates = _evaluator.MeasureDuplicateProximity(
            index, SampleWithGroup("d1", "d2"), topK: 2);

        Assert.Equal(1, duplicates.GroupsEvaluated);
        Assert.Equal(2, duplicates.SeedsEvaluated);
        Assert.Equal(0.0, duplicates.MeanRecall, precision: 10);
        Assert.Equal(0.0, duplicates.HitRate, precision: 10);
        Assert.Equal(0, duplicates.SeedsWithFullyDuplicatedTopK);
    }

    [Fact]
    public void Grupo_cujos_membros_nao_estao_no_indice_e_descartado_em_vez_de_zerar_a_media()
    {
        SimilarityIndex index = BuildIndex(Track("d1", 0.80, "pop"), Track("outra", 0.79, "pop"));

        DuplicateProximityProxy duplicates = _evaluator.MeasureDuplicateProximity(
            index, SampleWithGroup("d1", "ausente"), topK: 2);

        Assert.Equal(0, duplicates.GroupsEvaluated);
        Assert.Equal(0, duplicates.SeedsEvaluated);
    }

    [Fact]
    public void Amostra_sem_sementes_e_rejeitada_em_vez_de_produzir_proxy_perfeito_por_vacuidade()
    {
        DomainException exception = Assert.Throws<DomainException>(
            () => RecommenderEvaluationSample.Create([], []));

        Assert.Contains("vacuidade", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Grupo_de_duplicatas_com_um_unico_membro_e_rejeitado()
    {
        Assert.Throws<DomainException>(() => DuplicateTrackGroup.Create("artista|titulo", ["so-um"]));
    }

    [Fact]
    public void Peso_de_boost_negativo_e_rejeitado_na_configuracao_da_avaliacao()
    {
        Assert.Throws<DomainException>(() => RecommenderEvaluationSetting.BoostedBy(-0.01));
    }

    [Fact]
    public void Top_n_nao_positivo_e_rejeitado()
    {
        SimilarityIndex index = BuildIndex(Track("a", 0.80, "pop"), Track("b", 0.79, "pop"));

        Assert.Throws<DomainException>(
            () => _evaluator.Measure(index, SampleOf("a"), RecommenderEvaluationSetting.CosineOnly(), topN: 0));

        Assert.Throws<DomainException>(
            () => _evaluator.MeasureDuplicateProximity(index, SampleWithGroup("a", "b"), topK: 0));
    }

    // --- E4.9: o avaliador mede o over-fetch ADAPTATIVO, não a contagem de rodada única ---

    /// <summary>
    /// O teste anti-divergência do E4.9. O avaliador e o handler consomem o MESMO
    /// <see cref="RecommendationOverFetch"/>: se o endpoint adaptar a janela e o avaliador continuar na rodada única,
    /// o gate volta a defender um sistema que o endpoint não entrega — literalmente o bug que o E4.10 corrigiu neste
    /// módulo, com tudo verde por dois épicos.
    ///
    /// <para>O cenário é o mesmo do handler: um grupo de quase-duplicatas ocupa a janela da primeira rodada inteira e
    /// colapsa num item só. Com a rodada única o tamanho medido seria 1; com o adaptativo, a segunda rodada alcança as
    /// faixas distintas e o top-N fecha em 3.</para>
    /// </summary>
    [Fact]
    public void Tamanho_do_resultado_reflete_o_over_fetch_adaptativo_do_endpoint()
    {
        (SimilarityIndex index, RecommenderEvaluationContext context) = CollapsingWorld();

        RecommendationSizeProxy size = RecommenderQualityEvaluator.MeasureResultSize(
            index,
            SampleOf("seed"),
            RecommenderEvaluationSetting.CosineOnly().WithDedupe(),
            topN: 3,
            context);

        Assert.Equal(1, size.SeedsEvaluated);
        Assert.Equal(0, size.SeedsBelowTopN);
        Assert.Equal(0, size.SeedsWithSingleResult);
        Assert.Equal(3.0, size.MeanResultSize, precision: 10);
        Assert.True(size.MaximumRoundsUsed > 1, "A semente só alcança o top-N pedido com mais de uma rodada.");
    }

    /// <summary>
    /// O par do teste acima, pelo caminho por semente: o mesmo mundo, a mesma resposta, e o custo declarado. É este
    /// método que o instrumento de latência cronometra — medir latência por um caminho diferente do que produz o
    /// resultado seria medir outra coisa.
    /// </summary>
    [Fact]
    public void Ranking_por_semente_declara_quantas_rodadas_custou()
    {
        (SimilarityIndex index, RecommenderEvaluationContext context) = CollapsingWorld();

        EndpointTopN? ranked = RecommenderQualityEvaluator.RankAsEndpointWould(
            index, "seed", topN: 3, RecommenderEvaluationSetting.CosineOnly().WithDedupe(), context);

        Assert.NotNull(ranked);
        Assert.Equal(3, ranked.Neighbors.Count);
        Assert.Equal(2, ranked.RoundsUsed);

        // A janela da rodada 2 pediria 20 candidatas, mas o catálogo do fixture só tem 16 além da semente: o que é
        // reportado é a janela EFETIVA, não a pedida — senão o custo declarado seria maior que o trabalho feito.
        Assert.Equal(16, ranked.CandidatesConsidered);
        Assert.True(
            ranked.CandidatesConsidered < RecommendationOverFetch.CountForRound(3, round: 2),
            "O fixture precisa ser menor que a janela da rodada 2 para este ponto fazer sentido.");
    }

    [Fact]
    public void Semente_fora_do_indice_nao_produz_ranking_nem_entra_na_distribuicao()
    {
        SimilarityIndex index = BuildIndex(Track("a", 0.80, "pop"), Track("b", 0.70, "pop"));

        Assert.Null(RecommenderQualityEvaluator.RankAsEndpointWould(
            index, "ausente", topN: 2, RecommenderEvaluationSetting.CosineOnly()));

        RecommendationSizeProxy size = RecommenderQualityEvaluator.MeasureResultSize(
            index, SampleOf("ausente"), RecommenderEvaluationSetting.CosineOnly(), topN: 2);

        Assert.Equal(0, size.SeedsEvaluated);
        Assert.Equal(1, size.SeedsMissingFromIndex);
        Assert.Equal(0, size.MaximumRoundsUsed);
    }

    /// <summary>
    /// Um mundo em que a primeira janela do over-fetch é toda de quase-duplicatas. Com <c>topN=3</c> a rodada 1 pede
    /// 10 candidatas e encontra as 10 irmãs (que colapsam em 1); a rodada 2 pede 20 e alcança as faixas distintas.
    ///
    /// <para>O fixture colinear do E4.4 não serve aqui: nele TODO par tem cosseno 1 e o dedup colapsaria o catálogo
    /// inteiro num item, tornando o cenário impossível de distinguir de uma semente patológica.</para>
    /// </summary>
    private static (SimilarityIndex Index, RecommenderEvaluationContext Context) CollapsingWorld()
    {
        var tracks = new List<RawTrackFeatures> { Sibling("seed", 0) };
        var attributes = new Dictionary<string, EvaluationTrackAttributes>(StringComparer.Ordinal)
        {
            ["seed"] = new(RecommendationDuplicateKey.From("A Mesma Obra", "O Mesmo Artista"), Popularity: 50)
        };

        for (int i = 0; i < 10; i++)
        {
            string trackId = $"dup{i:00}";
            tracks.Add(Sibling(trackId, i + 1));
            attributes[trackId] = new EvaluationTrackAttributes(
                RecommendationDuplicateKey.From("A Mesma Obra", "O Mesmo Artista"), Popularity: 50);
        }

        for (int k = 0; k < 6; k++)
        {
            string trackId = $"far{k:00}";
            tracks.Add(new RawTrackFeatures(trackId, DistantVector(k), Genre: "pop", IsImputed: false));
            attributes[trackId] = new EvaluationTrackAttributes(
                RecommendationDuplicateKey.From($"Obra {trackId}", $"Artista {trackId}"), Popularity: 50);
        }

        return (
            SimilarityIndex.Build(tracks),
            new RecommenderEvaluationContext(
                attributes,
                new Dictionary<string, IReadOnlyList<BlendCollaborativeCandidate>>(StringComparer.Ordinal)));
    }

    /// <summary>Uma versão da mesma obra: o vetor da semente com perturbação de 1e-5, cosseno acima de 0,999.</summary>
    private static RawTrackFeatures Sibling(string trackId, int ordinal) =>
        new(
            trackId,
            SimilarityFeatureVector.Create(
            [
                0.80 + (ordinal * 1e-5), 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0
            ]),
            Genre: "pop",
            IsImputed: false);

    /// <summary>Vetores mutuamente distantes o bastante para NÃO colapsarem entre si, por rotação determinística.</summary>
    private static SimilarityFeatureVector DistantVector(int k)
    {
        static double Cycle(int k, int multiplier) => ((k * multiplier) % 7) / 6.0;

        return SimilarityFeatureVector.Create(
        [
            0.05 + (0.90 * Cycle(k, 3)),
            0.05 + (0.90 * Cycle(k, 5)),
            0.05 + (0.90 * Cycle(k, 6)),
            60.0 + (120.0 * Cycle(k, 2)),
            0.05 + (0.90 * Cycle(k, 4)),
            0.05 + (0.90 * Cycle(k, 1)),
            0.05 + (0.90 * Cycle(k, 5)),
            0.02 + (0.50 * Cycle(k, 3)),
            -30.0 + (28.0 * Cycle(k, 6))
        ]);
    }

    private static RecommenderEvaluationSample SampleOf(params string[] seedTrackIds) =>
        RecommenderEvaluationSample.Create(seedTrackIds, []);

    private static RecommenderEvaluationSample SampleWithGroup(params string[] trackIds) =>
        RecommenderEvaluationSample.Create(
            [trackIds[0]], [DuplicateTrackGroup.Create("artista|titulo", trackIds)]);

    private static SimilarityIndex BuildIndex(params RawTrackFeatures[] tracks) => SimilarityIndex.Build(tracks);

    /// <summary>
    /// Uma faixa cuja posição no espaço é controlada por um único escalar: as nove features escalam com
    /// <paramref name="intensity"/>.
    ///
    /// <para><b>Consequência que estes testes exploram de propósito:</b> vetores colineares na origem, depois de
    /// CENTRADOS pelo z-score, ficam todos sobre a mesma reta, logo o cosseno entre duas faixas é exatamente +1
    /// (intensidades do mesmo lado da média) ou -1 (lados opostos). Isso torna o ranking determinístico e calculável
    /// à mão — faixas de mesma intensidade são empate perfeito e vêm antes de qualquer faixa do outro lado —, que é
    /// o que permite afirmar o valor EXATO esperado de cada proxy sem depender de desempate arbitrário. Este fixture
    /// mede o instrumento de contagem; a riqueza do espaço real é coberta pelos testes do E4.1/E4.3.</para>
    /// </summary>
    private static RawTrackFeatures Track(string trackId, double intensity, string? genre, bool isImputed = false)
    {
        SimilarityFeatureVector vector = SimilarityFeatureVector.Create(
        [
            intensity,
            intensity,
            intensity,
            100.0 * intensity,
            intensity,
            intensity,
            intensity,
            intensity,
            -20.0 * intensity
        ]);

        return new RawTrackFeatures(trackId, vector, genre, isImputed);
    }
}
