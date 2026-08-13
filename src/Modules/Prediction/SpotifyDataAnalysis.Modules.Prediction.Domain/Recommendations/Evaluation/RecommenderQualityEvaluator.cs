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
    /// Mede os proxies 1 e 2 sobre a amostra, sob uma configuração de ranking. Uma varredura por semente serve aos
    /// dois proxies — o top-N é calculado uma vez e lido duas.
    /// </summary>
    /// <param name="index">O índice já montado; nunca é reconstruído aqui.</param>
    /// <param name="sample">A amostra fixa, a mesma em todas as configurações comparadas.</param>
    /// <param name="setting">A configuração sob medição (cosine puro ou boost com um peso).</param>
    /// <param name="topN">Tamanho do top-N avaliado.</param>
    /// <exception cref="DomainException">Quando <paramref name="topN"/> não é positivo.</exception>
    public RecommenderQualityMeasurement Measure(
        SimilarityIndex index, RecommenderEvaluationSample sample, RecommenderEvaluationSetting setting, int topN)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(setting);

        if (topN <= 0)
            throw new DomainException($"O top-N avaliado deve ser positivo. Recebido: {topN}.");

        int seedsMissingFromIndex = 0;
        int seedsWithoutUsableGenre = 0;
        int imputedSeeds = 0;
        int seedsEvaluated = 0;
        int saturatedSeeds = 0;
        int selfExclusionViolations = 0;
        int selfExclusionSeeds = 0;
        double coherenceSum = 0.0;
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
                index.FindNearestTo(seedTrackId, topN, policy) ?? Array.Empty<TrackSimilarity>();

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
            imputedNeighborSum += imputedNeighbors;
            seedsEvaluated++;

            if (sameGenreNeighbors == neighbors.Count)
                saturatedSeeds++;
        }

        var genreCoherence = new GenreCoherenceProxy(
            seedsEvaluated,
            seedsWithoutUsableGenre,
            seedsMissingFromIndex,
            imputedSeeds,
            seedsEvaluated == 0 ? 0.0 : coherenceSum / seedsEvaluated,
            saturatedSeeds,
            seedsEvaluated == 0 ? 0.0 : imputedNeighborSum / seedsEvaluated);

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
    /// <exception cref="DomainException">Quando <paramref name="topK"/> não é positivo.</exception>
    public DuplicateProximityProxy MeasureDuplicateProximity(
        SimilarityIndex index, RecommenderEvaluationSample sample, int topK)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(sample);

        if (topK <= 0)
            throw new DomainException($"O top-K do proxy de duplicatas deve ser positivo. Recebido: {topK}.");

        GenreAffinityPolicy cosineOnly = GenreAffinityPolicy.CosineOnly();

        int groupsEvaluated = 0;
        int seedsEvaluated = 0;
        int seedsWithHit = 0;
        int seedsFullyDuplicated = 0;
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
                    index.FindNearestTo(seedTrackId, topK, cosineOnly) ?? Array.Empty<TrackSimilarity>();

                seedsEvaluated++;

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
            seedsFullyDuplicated);
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
