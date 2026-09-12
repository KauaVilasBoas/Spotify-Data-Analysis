using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Blending;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Deduplication;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

/// <summary>
/// O instrumento de medição do recomendador (E4.4): calcula os três proxies verificáveis (DP-B) sobre um
/// <see cref="SimilarityIndex"/> já montado e uma amostra fixa. É um serviço de domínio SEM ESTADO — não guarda
/// índice, não guarda catálogo, não abre conexão; recebe tudo e devolve números.
///
/// <para><b>Por que não aloca nada relevante (free tier):</b> a única estrutura por semente é um
/// <see cref="HashSet{T}"/> do tamanho do top-N. O catálogo continua existindo em UMA cópia — a do índice cacheado
/// que o recomendador já usa em produção. Rodar a avaliação não é "montar um segundo recomendador para medir".</para>
///
/// <para><b>Por que a coerência é medida com os rótulos do índice, e não com o que a política boostou:</b> os
/// proxies precisam ser comparáveis entre <c>off</c> e cada peso. Se o proxy 1 lesse "quem recebeu bônus", ele
/// daria zero no modo off por construção e mediria a mecânica, não o resultado.</para>
/// </summary>
public sealed class RecommenderQualityEvaluator
{
    /// <summary>
    /// Fator de over-fetch quando há pós-processamento (dedup e/ou blend), espelhando
    /// <c>GetTrackRecommendationsQueryHandler.DedupeOverFetchFactor</c>.
    ///
    /// <para><b>Por que a constante é duplicada aqui:</b> a de produção é <c>private</c> dentro do handler da
    /// Application, e o Domain não referencia a Application. Medir com um over-fetch diferente do de produção
    /// mediria outro sistema — o dedup colapsaria a partir de um conjunto de candidatas diferente e o top-N
    /// resultante não seria o que o endpoint devolve. A duplicação é o custo consciente de não inverter a
    /// dependência; se o handler mudar o fator, este número precisa mudar junto.</para>
    /// </summary>
    public const int OverFetchFactor = 3;

    /// <summary>Piso do over-fetch, espelhando <c>GetTrackRecommendationsQueryHandler.MinimumDedupeOverFetch</c>.</summary>
    public const int MinimumOverFetch = 10;

    /// <summary>
    /// Mede os proxies 1 e 2 sobre a amostra, sob uma configuração de ranking. Uma varredura por semente serve aos
    /// dois proxies — o top-N é calculado uma vez e lido duas.
    /// </summary>
    /// <param name="index">O índice já montado; nunca é reconstruído aqui.</param>
    /// <param name="sample">A amostra fixa, a mesma em todas as configurações comparadas.</param>
    /// <param name="setting">A configuração sob medição (gênero, dedup e estratégia).</param>
    /// <param name="topN">Tamanho do top-N avaliado.</param>
    /// <param name="context">
    /// Os insumos de dedup e de blend. <see langword="null"/> equivale a <see cref="RecommenderEvaluationContext.Empty"/>
    /// — suficiente para o ranking cru, insuficiente para uma configuração que liga dedup ou blend.
    /// </param>
    /// <exception cref="DomainException">Quando <paramref name="topN"/> não é positivo.</exception>
    public RecommenderQualityMeasurement Measure(
        SimilarityIndex index,
        RecommenderEvaluationSample sample,
        RecommenderEvaluationSetting setting,
        int topN,
        RecommenderEvaluationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(setting);

        if (topN <= 0)
            throw new DomainException($"O top-N avaliado deve ser positivo. Recebido: {topN}.");

        RecommenderEvaluationContext effectiveContext = context ?? RecommenderEvaluationContext.Empty;

        int seedsMissingFromIndex = 0;
        int seedsWithoutUsableGenre = 0;
        int imputedSeeds = 0;
        int seedsEvaluated = 0;
        int saturatedSeeds = 0;
        int selfExclusionViolations = 0;
        int selfExclusionSeeds = 0;
        double coherenceSum = 0.0;
        double coherenceSquaredSum = 0.0;
        double imputedNeighborSum = 0.0;

        foreach (string seedTrackId in sample.SeedTrackIds)
        {
            bool? seedIsImputed = index.IsImputedTrack(seedTrackId);
            if (seedIsImputed is null)
            {
                seedsMissingFromIndex++;
                continue;
            }

            if (seedIsImputed.Value)
                imputedSeeds++;

            string? seedGenre = index.GenreOf(seedTrackId);

            GenreAffinityPolicy policy = setting.PolicyFor(seedGenre, seedIsImputed.Value);
            IReadOnlyList<TrackSimilarity> neighbors =
                RankAsProduction(index, seedTrackId, topN, policy, setting, effectiveContext)
                ?? Array.Empty<TrackSimilarity>();

            selfExclusionSeeds++;
            if (ContainsSeed(neighbors, seedTrackId))
                selfExclusionViolations++;

            bool seedGenreIsUsable = !string.IsNullOrWhiteSpace(seedGenre) && !seedIsImputed.Value;
            if (!seedGenreIsUsable || neighbors.Count == 0)
            {
                seedsWithoutUsableGenre++;
                continue;
            }

            int sameGenreNeighbors = 0;
            int imputedNeighbors = 0;

            foreach (TrackSimilarity neighbor in neighbors)
            {
                if (string.Equals(index.GenreOf(neighbor.TrackId), seedGenre, StringComparison.Ordinal))
                    sameGenreNeighbors++;

                if (neighbor.IsImputed)
                    imputedNeighbors++;
            }

            double coherence = (double)sameGenreNeighbors / neighbors.Count;

            coherenceSum += coherence;
            coherenceSquaredSum += coherence * coherence;
            imputedNeighborSum += imputedNeighbors;
            seedsEvaluated++;

            if (sameGenreNeighbors == neighbors.Count)
                saturatedSeeds++;
        }

        double meanCoherence = seedsEvaluated == 0 ? 0.0 : coherenceSum / seedsEvaluated;

        // Desvio populacional entre sementes. O max(0, ...) protege o cancelamento catastrófico da forma
        // E[x²] − E[x]²: com coerências concentradas perto de 0 ou de 1 a diferença pode sair negativa por erro de
        // ponto flutuante, e um Sqrt de negativo viraria NaN silencioso no meio de um relatório de qualidade.
        double coherenceVariance = seedsEvaluated == 0
            ? 0.0
            : Math.Max(0.0, (coherenceSquaredSum / seedsEvaluated) - (meanCoherence * meanCoherence));

        var genreCoherence = new GenreCoherenceProxy(
            seedsEvaluated,
            seedsWithoutUsableGenre,
            seedsMissingFromIndex,
            imputedSeeds,
            meanCoherence,
            saturatedSeeds,
            seedsEvaluated == 0 ? 0.0 : imputedNeighborSum / seedsEvaluated,
            Math.Sqrt(coherenceVariance));

        return new RecommenderQualityMeasurement(
            setting, topN, genreCoherence, new SelfExclusionProxy(selfExclusionSeeds, selfExclusionViolations));
    }

    /// <summary>
    /// Mede o proxy 3 sobre os grupos de quase-duplicatas, SEMPRE com o gênero desligado: duplicatas compartilham o
    /// gênero por construção, então medi-las com o boost ligado contaminaria justamente o proxy que deveria ser
    /// independente de gênero. O modo não é parâmetro de propósito — não é uma escolha do chamador.
    /// </summary>
    /// <param name="index">O índice já montado.</param>
    /// <param name="sample">A amostra fixa, de onde saem os grupos.</param>
    /// <param name="topK">Tamanho do top-K em que os irmãos devem se reencontrar.</param>
    /// <param name="setting">
    /// A configuração do top-K medido. Só <c>Dedupe</c> e <c>BlendWeight</c> têm efeito aqui — o gênero é sempre
    /// desligado. <see langword="null"/> equivale a <see cref="RecommenderEvaluationSetting.CosineOnly"/>, que é o
    /// motor de similaridade cru do E4.4.
    /// </param>
    /// <param name="context">Insumos de dedup/blend; obrigatório quando <paramref name="setting"/> liga um dos dois.</param>
    /// <exception cref="DomainException">Quando <paramref name="topK"/> não é positivo.</exception>
    public DuplicateProximityProxy MeasureDuplicateProximity(
        SimilarityIndex index,
        RecommenderEvaluationSample sample,
        int topK,
        RecommenderEvaluationSetting? setting = null,
        RecommenderEvaluationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(sample);

        if (topK <= 0)
            throw new DomainException($"O top-K do proxy de duplicatas deve ser positivo. Recebido: {topK}.");

        // O proxy 3 é SEMPRE medido com o gênero desligado (o único guarda não circular), então a configuração
        // recebida tem o modo de gênero forçado para off — o que sobra dela é o dedup e o blend.
        RecommenderEvaluationSetting effectiveSetting = RecommenderEvaluationSetting.CosineOnly()
            .WithDedupe((setting ?? RecommenderEvaluationSetting.CosineOnly()).Dedupe);

        if (setting?.BlendWeight is double blendWeight)
            effectiveSetting = effectiveSetting.WithBlend(blendWeight);

        RecommenderEvaluationContext effectiveContext = context ?? RecommenderEvaluationContext.Empty;
        GenreAffinityPolicy cosineOnly = GenreAffinityPolicy.CosineOnly();

        int groupsEvaluated = 0;
        int seedsEvaluated = 0;
        int seedsWithHit = 0;
        int seedsFullyDuplicated = 0;
        int seedsWithRedundantSiblings = 0;
        int seedsWithIncompleteTopK = 0;
        int siblingsFound = 0;
        double recallSum = 0.0;
        double firstRankSum = 0.0;
        double siblingCosineSum = 0.0;

        foreach (DuplicateTrackGroup group in sample.DuplicateGroups)
        {
            var indexedMembers = new HashSet<string>(StringComparer.Ordinal);
            foreach (string trackId in group.TrackIds)
            {
                if (index.ContainsTrack(trackId))
                    indexedMembers.Add(trackId);
            }

            if (indexedMembers.Count < 2)
                continue;

            groupsEvaluated++;

            int reachableSiblings = Math.Min(indexedMembers.Count - 1, topK);

            foreach (string seedTrackId in indexedMembers)
            {
                IReadOnlyList<TrackSimilarity> neighbors =
                    RankAsProduction(index, seedTrackId, topK, cosineOnly, effectiveSetting, effectiveContext)
                    ?? Array.Empty<TrackSimilarity>();

                seedsEvaluated++;

                // A semente pediu topK e recebeu menos: com dedup ligado, um grupo de quase-duplicatas grande
                // consome todas as candidatas do over-fetch e colapsa num único item. Medido, e não inferido, porque
                // "o top-K inteiro é irmão" pode significar tanto repetição quanto lista curta — coisas opostas.
                if (neighbors.Count < topK)
                    seedsWithIncompleteTopK++;

                int hits = 0;
                int firstRank = 0;

                for (int rank = 0; rank < neighbors.Count; rank++)
                {
                    TrackSimilarity neighbor = neighbors[rank];
                    if (!indexedMembers.Contains(neighbor.TrackId))
                        continue;

                    hits++;
                    siblingsFound++;
                    siblingCosineSum += neighbor.CosineSimilarity;

                    if (firstRank == 0)
                        firstRank = rank + 1;
                }

                recallSum += (double)hits / reachableSiblings;

                if (hits > 0)
                {
                    seedsWithHit++;
                    firstRankSum += firstRank;
                }

                // DUAS versões da mesma obra ocupando duas posições do MESMO top-K: é exatamente o que o dedup do
                // E4.7 promete eliminar. O ground truth aqui é a `match_key` do Catalog, uma normalização escrita
                // independente da chave "artista|título" que o dedup reconstrói — por isso o indicador não é
                // circular: se as duas normalizações divergirem, é aqui que aparece.
                if (hits >= 2)
                    seedsWithRedundantSiblings++;

                if (neighbors.Count > 0 && hits == neighbors.Count)
                    seedsFullyDuplicated++;
            }
        }

        return new DuplicateProximityProxy(
            groupsEvaluated,
            seedsEvaluated,
            seedsEvaluated == 0 ? 0.0 : recallSum / seedsEvaluated,
            seedsEvaluated == 0 ? 0.0 : (double)seedsWithHit / seedsEvaluated,
            seedsWithHit == 0 ? 0.0 : firstRankSum / seedsWithHit,
            siblingsFound == 0 ? 0.0 : siblingCosineSum / siblingsFound,
            seedsFullyDuplicated,
            seedsWithRedundantSiblings,
            seedsWithIncompleteTopK,
            effectiveSetting);
    }

    /// <summary>
    /// O top-N que o ENDPOINT devolveria para esta semente sob esta configuração: over-fetch, blend colaborativo e
    /// dedup, na mesma ordem do <c>GetTrackRecommendationsQueryHandler</c>. Sem dedup nem blend, é literalmente o
    /// <see cref="SimilarityIndex.FindNearestTo(string,int,GenreAffinityPolicy)"/> do E4.1/E4.3 — o caminho medido
    /// pelo E4.4 permanece bit a bit o mesmo.
    ///
    /// <para>Devolve <see langword="null"/> quando a semente não está no índice, preservando a distinção que o
    /// chamador conta a parte.</para>
    /// </summary>
    private static IReadOnlyList<TrackSimilarity>? RankAsProduction(
        SimilarityIndex index,
        string seedTrackId,
        int topN,
        GenreAffinityPolicy policy,
        RecommenderEvaluationSetting setting,
        RecommenderEvaluationContext context)
    {
        bool postProcesses = setting.Dedupe || setting.IsBlended;
        int fetchCount = postProcesses ? Math.Max(topN * OverFetchFactor, MinimumOverFetch) : topN;

        IReadOnlyList<TrackSimilarity>? neighbors = index.FindNearestTo(seedTrackId, fetchCount, policy);
        if (neighbors is null || !postProcesses)
            return neighbors;

        // Cobertura colaborativa ZERO cai no content puro, exatamente como o handler (`coOccurring.Count > 0`).
        // Sem esta guarda a avaliação blendaria mesmo sem sinal — e, como o blender reordena pelo cosseno
        // RESCALADO em vez do score híbrido, o bônus de gênero sumiria do ranking de sementes que em produção
        // nunca chegam a blendar. Mediria um sistema que o endpoint não entrega.
        IReadOnlyList<BlendCollaborativeCandidate> collaborative = context.CollaborativeFor(seedTrackId);

        IReadOnlyList<TrackSimilarity> ranked = setting.BlendWeight is double blendWeight && collaborative.Count > 0
            ? BlendRanking(index, seedTrackId, neighbors, collaborative, blendWeight)
            : neighbors;

        if (setting.Dedupe)
            ranked = DedupeRanking(index, ranked, context, topN);

        return ranked.Count <= topN ? ranked : ranked.Take(topN).ToArray();
    }

    /// <summary>
    /// Reordena as candidatas pelo <see cref="RecommendationBlender"/> de produção (E4.6). Faixas só-colaborativas
    /// entram no ranking sem cosseno de áudio: o score delas é o próprio Jaccard, e a marca de imputação sai do
    /// índice quando elas estão indexadas. É o mesmo que o handler faz ao montar os <c>RankedCandidate</c>.
    /// </summary>
    private static List<TrackSimilarity> BlendRanking(
        SimilarityIndex index,
        string seedTrackId,
        IReadOnlyList<TrackSimilarity> neighbors,
        IReadOnlyList<BlendCollaborativeCandidate> collaborative,
        double blendWeight)
    {
        var contentCandidates = new List<BlendContentCandidate>(neighbors.Count);
        var byTrackId = new Dictionary<string, TrackSimilarity>(neighbors.Count, StringComparer.Ordinal);

        foreach (TrackSimilarity neighbor in neighbors)
        {
            // Neighbor null de propósito: o blender só o repassa adiante, e a avaliação não lê explicabilidade —
            // montar a decomposição por feature aqui seria custo puro sem efeito no ranking medido.
            contentCandidates.Add(new BlendContentCandidate(neighbor.TrackId, neighbor.CosineSimilarity, null));
            byTrackId[neighbor.TrackId] = neighbor;
        }

        IReadOnlyList<BlendedRecommendation> blended = new RecommendationBlender(blendWeight)
            .Blend(seedTrackId, contentCandidates, collaborative, int.MaxValue);

        var ranked = new List<TrackSimilarity>(blended.Count);
        foreach (BlendedRecommendation item in blended)
        {
            ranked.Add(byTrackId.TryGetValue(item.TrackId, out TrackSimilarity? neighbor)
                ? neighbor
                : new TrackSimilarity(
                    item.TrackId, item.Jaccard, CosineSimilarity: 0.0, GenreBonus: 0.0,
                    IsImputed: index.IsImputedTrack(item.TrackId) ?? false));
        }

        return ranked;
    }

    /// <summary>
    /// Colapsa quase-duplicatas do ranking over-fetched com o <see cref="NearDuplicateCollapser"/> de produção
    /// (E4.7), pelos mesmos dois critérios em OU (cosseno ≥ 0,999 do índice, ou a chave "artista|título") e com o
    /// mesmo desempate de representante (DP-2). Os representantes voltam como as vizinhas ORIGINAIS de mesmo id,
    /// idêntico ao <c>byTrackId[...]</c> do handler.
    /// </summary>
    private static List<TrackSimilarity> DedupeRanking(
        SimilarityIndex index,
        IReadOnlyList<TrackSimilarity> ranked,
        RecommenderEvaluationContext context,
        int topN)
    {
        var byTrackId = new Dictionary<string, TrackSimilarity>(ranked.Count, StringComparer.Ordinal);
        var candidates = new List<DeduplicationCandidate>(ranked.Count);

        foreach (TrackSimilarity neighbor in ranked)
        {
            byTrackId[neighbor.TrackId] = neighbor;
            EvaluationTrackAttributes attributes = context.AttributesOf(neighbor.TrackId);

            candidates.Add(new DeduplicationCandidate(
                new ExplainedTrackSimilarity(
                    neighbor.TrackId,
                    neighbor.Similarity,
                    neighbor.CosineSimilarity,
                    neighbor.GenreBonus,
                    neighbor.GenreBonus > 0,
                    neighbor.IsImputed,
                    []),
                attributes.DuplicateKey,
                attributes.Popularity,
                neighbor.IsImputed));
        }

        var collapser = new NearDuplicateCollapser((a, b) => index.CosineBetween(a, b) ?? 0.0);

        IReadOnlyList<CollapsedRecommendation> collapsed = collapser.Collapse(candidates, topN);

        var representatives = new List<TrackSimilarity>(collapsed.Count);
        foreach (CollapsedRecommendation item in collapsed)
            representatives.Add(byTrackId[item.Representative.TrackId]);

        return representatives;
    }

    private static bool ContainsSeed(IReadOnlyList<TrackSimilarity> neighbors, string seedTrackId)
    {
        foreach (TrackSimilarity neighbor in neighbors)
        {
            if (string.Equals(neighbor.TrackId, seedTrackId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
