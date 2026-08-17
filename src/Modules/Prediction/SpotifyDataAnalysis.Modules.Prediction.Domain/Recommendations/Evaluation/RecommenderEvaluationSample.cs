using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

/// <summary>
/// O insumo FIXO da avaliação do recomendador (E4.4): as sementes sorteadas do catálogo e os grupos de
/// quase-duplicatas. É deliberadamente um dado de entrada, e não algo que o avaliador sorteia por conta própria —
/// a reprodutibilidade exige que a MESMA amostra atravesse todas as configurações medidas (cosine puro e cada peso
/// de boost), senão a variação da amostra se confundiria com o efeito do peso.
///
/// <para><b>Por que a amostra é uma lista de ids, e não de faixas:</b> a restrição de free tier proíbe uma segunda
/// cópia do catálogo em memória. O avaliador resolve cada id contra o índice JÁ montado; a amostra em si custa
/// alguns milhares de strings.</para>
/// </summary>
/// <param name="SeedTrackIds">Sementes da avaliação, na ordem determinística em que foram sorteadas.</param>
/// <param name="DuplicateGroups">Grupos de faixas que o catálogo considera a mesma obra (proxy 3).</param>
public sealed record RecommenderEvaluationSample(
    IReadOnlyList<string> SeedTrackIds,
    IReadOnlyList<DuplicateTrackGroup> DuplicateGroups)
{
    /// <summary>
    /// Monta a amostra validando que ela mede alguma coisa: uma amostra sem sementes produziria proxies "perfeitos"
    /// por vacuidade, que é exatamente o tipo de número mentiroso que este card existe para impedir.
    /// </summary>
    /// <exception cref="DomainException">Quando não há semente alguma para avaliar.</exception>
    public static RecommenderEvaluationSample Create(
        IReadOnlyList<string> seedTrackIds, IReadOnlyList<DuplicateTrackGroup> duplicateGroups)
    {
        ArgumentNullException.ThrowIfNull(seedTrackIds);
        ArgumentNullException.ThrowIfNull(duplicateGroups);

        if (seedTrackIds.Count == 0)
            throw new DomainException(
                "A amostra de avaliação não tem sementes: um proxy medido sobre zero sementes daria 100% por " +
                "vacuidade, não por qualidade.");

        return new RecommenderEvaluationSample(seedTrackIds, duplicateGroups);
    }
}

/// <summary>
/// Um grupo de faixas que o catálogo reconhece como a MESMA obra (mesma <c>match_key</c>: artista principal +
/// título normalizados), com ids distintos. É o sinal de verdade do proxy 3 — o único proxy independente de gênero
/// e, portanto, o único que testa o espaço de similaridade sem circularidade.
///
/// <para><b>Ressalva de instrumento:</b> a <c>match_key</c> é heurística e agrupa "mesma obra", não "mesma
/// gravação" — regravações do mesmo artista caem no mesmo grupo. Isso não invalida o proxy (continuam sendo faixas
/// que DEVEM ser vizinhas), mas explica por que o alvo honesto do proxy não é 100% cravado.</para>
/// </summary>
/// <param name="MatchKey">A chave que agrupou as faixas, guardada para a inspeção manual dos grupos.</param>
/// <param name="TrackIds">Os ids das faixas do grupo; sempre dois ou mais.</param>
public sealed record DuplicateTrackGroup(string MatchKey, IReadOnlyList<string> TrackIds)
{
    /// <summary>Quantos pares distintos o grupo representa — <c>n·(n-1)/2</c>, o número que dimensiona o E4.7.</summary>
    public int PairCount => TrackIds.Count * (TrackIds.Count - 1) / 2;

    /// <summary>
    /// Monta o grupo exigindo pelo menos dois membros: um "grupo" de um só não tem irmão para reencontrar e
    /// contaminaria o proxy com sementes cujo recall é indefinido.
    /// </summary>
    /// <exception cref="DomainException">Quando o grupo tem menos de duas faixas.</exception>
    public static DuplicateTrackGroup Create(string matchKey, IReadOnlyList<string> trackIds)
    {
        ArgumentNullException.ThrowIfNull(trackIds);

        if (trackIds.Count < 2)
            throw new DomainException(
                $"O grupo de duplicatas '{matchKey}' tem {trackIds.Count} faixa(s): sem um irmão para reencontrar, " +
                "não há o que medir.");

        return new DuplicateTrackGroup(matchKey, trackIds);
    }
}
