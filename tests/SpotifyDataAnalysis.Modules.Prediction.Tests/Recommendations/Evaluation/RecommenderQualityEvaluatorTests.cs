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
        Assert.Equal(0, measurement.GenreCoherence.SeedsWithEmptyTopN);
        Assert.Equal(0, measurement.GenreCoherence.SeedsMissingFromIndex);
    }

    /// <summary>
    /// E4.12: as duas causas de descarte são contadas SEPARADAMENTE. Aqui a semente TEM gênero utilizável e o top-N sai
    /// vazio por causa do FUNIL — não há outra faixa elegível para o ranking devolver.
    ///
    /// <para>Com o contador único, esta semente era publicada em <c>SeedsWithoutUsableGenre</c>, cujo
    /// <c>&lt;summary&gt;</c> dizia "sem gênero" e cuja coluna do relatório se chama <c>sem_genero</c>: quem lê a tabela
    /// concluiria "falta rótulo de gênero" quando a causa foi o funil não ter entregado vizinho. O gate não consome o
    /// campo, então o efeito é de análise — e é análise que orienta decisão de produto.</para>
    /// </summary>
    [Fact]
    public void Top_n_vazio_nao_e_contado_como_semente_sem_genero()
    {
        SimilarityIndex index = BuildIndex(Track("seed", 0.80, "pop"));

        RecommenderQualityMeasurement measurement = _evaluator.Measure(
            index, SampleOf("seed"), RecommenderEvaluationSetting.CosineOnly(), topN: 3);

        // As premissas do cenário: a semente está no índice, TEM gênero, e o top-N dela é vazio.
        Assert.Equal("pop", index.GenreOf("seed"));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<TrackSimilarity>>(
            index.FindNearestTo("seed", topN: 3)));

        Assert.Equal(0, measurement.GenreCoherence.SeedsEvaluated);
        Assert.Equal(0, measurement.GenreCoherence.SeedsMissingFromIndex);
        Assert.Equal(0, measurement.GenreCoherence.SeedsWithoutUsableGenre);
        Assert.Equal(1, measurement.GenreCoherence.SeedsWithEmptyTopN);
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

    // --- E4.12: a janela da rodada corta o FUNIL INTEIRO também no avaliador, não só o lado de áudio ---

    /// <summary>
    /// O mesmo defeito que o <c>b226f7a</c> corrigiu no handler, espelhado aqui. Depois do E4.9 o avaliador recortava
    /// pela janela só a varredura de áudio e entregava a lista colaborativa INTEIRA a todas as rodadas: a rodada 1 do
    /// blend disputava com o top-máximo colaborativo (120 candidatas para <c>topN=10</c>) em vez do top-30. Era inerte
    /// porque o gate recusa medição blendada — mas no instante em que alguém habilitasse a medição do blend, o avaliador
    /// mediria um funil que o endpoint não entrega, que é exatamente a divergência que o E4.10 pagou para corrigir.
    ///
    /// <para>O cenário é o do handler: as 30 primeiras colaborativas são forasteiras (fora do índice) com Jaccard
    /// calibrado para NÃO alcançarem o top-10, e a 31ª — a primeira além da janela da rodada 1 — é a faixa que o
    /// ranking de conteúdo deixou em 11º. Se o lado colaborativo vazar além da janela, ela ganha a parcela colaborativa,
    /// passa a 10ª e a composição do top-10 muda sem nenhuma rodada 2 ter ocorrido.</para>
    /// </summary>
    [Fact]
    public void Primeira_rodada_do_blend_ignora_o_colaborativo_alem_da_janela()
    {
        const int topN = 10;
        const double collaborativeWeight = RecommendationBlender.DefaultCollaborativeWeight;
        const double contentWeight = 1.0 - collaborativeWeight;
        int firstWindow = RecommendationOverFetch.CountForRound(topN, round: 1);

        SimilarityIndex index = SpreadIndex(40);

        // O ranking de conteúdo puro é a régua: quem está no top-10 e quem é o primeiro de fora.
        EndpointTopN? contentRanking = RecommenderQualityEvaluator.RankAsEndpointWould(
            index, "seed", topN: 40, RecommenderEvaluationSetting.CosineOnly());

        Assert.NotNull(contentRanking);

        string[] contentTopN = contentRanking.Neighbors.Take(topN).Select(item => item.TrackId).ToArray();
        string firstOutsideTopN = contentRanking.Neighbors[topN].TrackId;

        // A parcela de content do blend é o min-max do score de conteúdo DENTRO da janela da rodada 1.
        double[] windowScores = contentRanking.Neighbors
            .Take(firstWindow).Select(item => item.Similarity).ToArray();
        double lowest = windowScores.Min();
        double spread = windowScores.Max() - lowest;
        double NormalizedContent(double score) => (score - lowest) / spread;

        double lastInTopN = contentWeight * NormalizedContent(windowScores[topN - 1]);
        double firstOutside = contentWeight * NormalizedContent(windowScores[topN]);

        // Jaccard que vale METADE do que a última do top-10 já vale: forte o bastante para promover a 11ª acima da 10ª,
        // fraco o bastante para uma forasteira sem sinal de áudio nenhum não alcançar o top-10.
        double jaccard = lastInTopN / (2.0 * collaborativeWeight);

        Assert.True(
            jaccard is > 0.0 and <= 1.0,
            $"Premissa do cenário: o Jaccard calibrado tem de ser um sinal válido. Calculado: {jaccard}.");
        Assert.True(
            collaborativeWeight * jaccard < lastInTopN,
            "Premissa do cenário: uma forasteira só-colaborativa com esse Jaccard fica FORA do top-10 " +
            $"({collaborativeWeight * jaccard} vs {lastInTopN}).");
        Assert.True(
            firstOutside + (collaborativeWeight * jaccard) > lastInTopN,
            "Premissa do cenário: se o lado colaborativo vazar, a 11ª do conteúdo passa a 10ª " +
            $"({firstOutside + (collaborativeWeight * jaccard)} vs {lastInTopN}).");

        var collaborative = new List<BlendCollaborativeCandidate>();
        for (int i = 0; i < firstWindow; i++)
            collaborative.Add(new BlendCollaborativeCandidate($"outsider{i:00}", CoPlaylists: 9, jaccard));

        // A 31ª candidata: mesmo Jaccard, menos playlists — a fonte ordena por jaccard DESC, co_playlists DESC, então
        // ela é legitimamente a última e cai fora da janela da rodada 1.
        collaborative.Add(new BlendCollaborativeCandidate(firstOutsideTopN, CoPlaylists: 1, jaccard));

        var context = new RecommenderEvaluationContext(
            new Dictionary<string, EvaluationTrackAttributes>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<BlendCollaborativeCandidate>>(StringComparer.Ordinal)
            {
                ["seed"] = collaborative
            });

        EndpointTopN? blended = RecommenderQualityEvaluator.RankAsEndpointWould(
            index,
            "seed",
            topN,
            RecommenderEvaluationSetting.CosineOnly().WithBlend(collaborativeWeight),
            context);

        Assert.NotNull(blended);
        Assert.Equal(1, blended.RoundsUsed);
        Assert.Equal(contentTopN, blended.Neighbors.Select(item => item.TrackId).ToArray());
    }

    /// <summary>
    /// O outro lado do critério: alargar a rodada tem de alargar AS DUAS listas. Aqui o lado de áudio está esgotado
    /// (27 vizinhas, das quais 25 são a mesma obra) e é só o lado colaborativo que ainda tem candidatas distintas a
    /// oferecer — além da janela da rodada 1. Se a janela recortasse o lado colaborativo mas o teto do laço continuasse
    /// sendo o tamanho da varredura de áudio, a rodada nunca alcançaria essas candidatas e o proxy 4 mediria 4
    /// recomendações para um <c>topN=10</c> que o endpoint entrega cheio.
    /// </summary>
    [Fact]
    public void Rodada_mais_larga_alarga_o_lado_colaborativo_tambem()
    {
        var tracks = new List<RawTrackFeatures> { Sibling("seed", 0) };
        var attributes = new Dictionary<string, EvaluationTrackAttributes>(StringComparer.Ordinal)
        {
            ["seed"] = new(RecommendationDuplicateKey.From("A Mesma Obra", "O Mesmo Artista"), Popularity: 50)
        };

        for (int i = 0; i < 25; i++)
        {
            string trackId = $"dup{i:00}";
            tracks.Add(Sibling(trackId, i + 1));
            attributes[trackId] = new EvaluationTrackAttributes(
                RecommendationDuplicateKey.From("A Mesma Obra", "O Mesmo Artista"), Popularity: 50);
        }

        for (int k = 0; k < 2; k++)
        {
            string trackId = $"far{k:00}";
            tracks.Add(new RawTrackFeatures(trackId, DistantVector(k), Genre: "pop", IsImputed: false));
            attributes[trackId] = new EvaluationTrackAttributes(
                RecommendationDuplicateKey.From($"Obra {trackId}", $"Artista {trackId}"), Popularity: 50);
        }

        // 27 forasteiras da MESMA obra (colapsam em 1) e, depois delas, 13 obras distintas: as distintas só existem
        // além da janela da rodada 1 (30).
        var collaborative = new List<BlendCollaborativeCandidate>();
        for (int i = 0; i < 27; i++)
        {
            string trackId = $"clone{i:00}";
            collaborative.Add(new BlendCollaborativeCandidate(trackId, CoPlaylists: 20, Jaccard: 0.50));
            attributes[trackId] = new EvaluationTrackAttributes(
                RecommendationDuplicateKey.From("A Obra Clonada", "O Artista Clonado"), Popularity: 50);
        }

        for (int i = 0; i < 13; i++)
        {
            string trackId = $"solo{i:00}";
            collaborative.Add(new BlendCollaborativeCandidate(trackId, CoPlaylists: 5, Jaccard: 0.40));
            attributes[trackId] = new EvaluationTrackAttributes(
                RecommendationDuplicateKey.From($"Obra Solo {i:00}", $"Artista Solo {i:00}"), Popularity: 50);
        }

        var context = new RecommenderEvaluationContext(
            attributes,
            new Dictionary<string, IReadOnlyList<BlendCollaborativeCandidate>>(StringComparer.Ordinal)
            {
                ["seed"] = collaborative
            });

        EndpointTopN? blended = RecommenderQualityEvaluator.RankAsEndpointWould(
            index: SimilarityIndex.Build(tracks),
            seedTrackId: "seed",
            topN: 10,
            RecommenderEvaluationSetting.CosineOnly().WithDedupe().WithBlend(0.35),
            context);

        Assert.NotNull(blended);
        Assert.Equal(10, blended.Neighbors.Count);
        Assert.True(blended.RoundsUsed > 1, "A janela da rodada 1 só alcança 3 obras distintas do lado colaborativo.");
    }

    /// <summary>
    /// Índice de vetores mutuamente espalhados, gerado por uma rotação determinística módulo 41 (primo) das nove
    /// features: os cossenos com a semente ficam distintos e bem separados, o que dá um ranking de conteúdo estável sem
    /// quase-duplicata nenhuma — a condição para o min-max do blend ter espalhamento e para o dedup não interferir.
    /// </summary>
    private static SimilarityIndex SpreadIndex(int count)
    {
        var tracks = new List<RawTrackFeatures>
        {
            new(
                "seed",
                SimilarityFeatureVector.Create([0.80, 0.80, 0.80, 120.0, 0.10, 0.10, 0.10, 0.05, -8.0]),
                Genre: "pop",
                IsImputed: false)
        };

        for (int k = 0; k < count; k++)
            tracks.Add(new RawTrackFeatures($"c{k:00}", SpreadVector(k), Genre: "pop", IsImputed: false));

        return SimilarityIndex.Build(tracks);
    }

    private static SimilarityFeatureVector SpreadVector(int k)
    {
        static double Cycle(int k, int multiplier) => ((k * multiplier) % 41) / 40.0;

        return SimilarityFeatureVector.Create(
        [
            0.05 + (0.90 * Cycle(k, 17)),
            0.05 + (0.90 * Cycle(k, 23)),
            0.05 + (0.90 * Cycle(k, 29)),
            60.0 + (120.0 * Cycle(k, 11)),
            0.05 + (0.90 * Cycle(k, 31)),
            0.05 + (0.90 * Cycle(k, 7)),
            0.05 + (0.90 * Cycle(k, 37)),
            0.02 + (0.50 * Cycle(k, 13)),
            -30.0 + (28.0 * Cycle(k, 19))
        ]);
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
