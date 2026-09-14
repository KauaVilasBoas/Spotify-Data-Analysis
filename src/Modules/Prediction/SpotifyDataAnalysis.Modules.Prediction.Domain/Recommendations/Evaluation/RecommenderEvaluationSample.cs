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
/// <param name="SeedPopulation">
/// QUE população a lista de sementes representa (E4.12). É identidade, não rótulo decorativo: o gate cobra a
/// população de cada medição que consome sementes, porque limiar calibrado numa população não vale noutra — a lista
/// curta é fenômeno dos grupos grandes de quase-duplicatas e é indistinguível de zero numa amostra sorteada.
///
/// <para>O default é <see cref="RecommenderEvaluationPopulation.Unspecified"/> de propósito, e isso é o fail-safe:
/// uma amostra que não declara a própria população nunca satisfaz uma guarda do gate.</para>
/// </param>
public sealed record RecommenderEvaluationSample(
    IReadOnlyList<string> SeedTrackIds,
    IReadOnlyList<DuplicateTrackGroup> DuplicateGroups,
    RecommenderEvaluationPopulation SeedPopulation = RecommenderEvaluationPopulation.Unspecified)
{
    /// <summary>
    /// Monta a amostra validando que ela mede alguma coisa: uma amostra sem sementes produziria proxies "perfeitos"
    /// por vacuidade, que é exatamente o tipo de número mentiroso que este card existe para impedir.
    /// </summary>
    /// <exception cref="DomainException">Quando não há semente alguma para avaliar.</exception>
    public static RecommenderEvaluationSample Create(
        IReadOnlyList<string> seedTrackIds,
        IReadOnlyList<DuplicateTrackGroup> duplicateGroups,
        RecommenderEvaluationPopulation seedPopulation = RecommenderEvaluationPopulation.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(seedTrackIds);
        ArgumentNullException.ThrowIfNull(duplicateGroups);

        if (seedTrackIds.Count == 0)
            throw new DomainException(
                "A amostra de avaliação não tem sementes: um proxy medido sobre zero sementes daria 100% por " +
                "vacuidade, não por qualidade.");

        return new RecommenderEvaluationSample(seedTrackIds, duplicateGroups, seedPopulation);
    }
}

/// <summary>
/// QUE população de sementes uma amostra representa — o eixo que faltava na identidade de uma medição (E4.12).
///
/// <para><b>Por que existe:</b> a configuração (<see cref="RecommenderEvaluationSetting"/>) e o top-N já viajavam com
/// cada proxy, mas a AMOSTRA não. Duas medições podiam ter configuração idêntica e vir de populações diferentes, e o
/// gate aprovava — com o agravante de que os limiares de completude do resultado foram calibrados sobre a população
/// PATOLÓGICA (0,3583 de lista curta antes do over-fetch adaptativo) e são satisfeitos por vacuidade numa amostra
/// sorteada, onde o fenômeno é ~0. Um conjunto fechado, e não um texto livre, porque o gate precisa RECONHECER a
/// população que calibrou cada régua.</para>
/// </summary>
public enum RecommenderEvaluationPopulation
{
    /// <summary>
    /// Não declarada. Nunca satisfaz uma guarda do gate — amostra sem população declarada é medição sem identidade.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// As sementes sorteadas do catálogo elegível pela semente fixa do sorteio — a população da coerência de gênero,
    /// da autoexclusão e da tabela de calibração do boost.
    /// </summary>
    RandomCatalogSeeds = 1,

    /// <summary>
    /// O subconjunto das sorteadas que TEM co-ocorrência registrada — o único recorte em que o blend muda o ranking.
    /// É população própria, e não as sorteadas: a média dela não é comparável com a da amostra inteira.
    /// </summary>
    CollaborativeCoveredSeeds = 2,

    /// <summary>
    /// UMA semente por grupo grande de quase-duplicatas (o primeiro membro de cada grupo). Serve ao proxy 3, que varre
    /// os membros por dentro dos grupos — NÃO é a população por semente dos grupos grandes.
    /// </summary>
    LargeNearDuplicateGroupLeaders = 3,

    /// <summary>
    /// TODOS os membros indexados dos grupos grandes de quase-duplicatas — a população patológica em que a lista curta
    /// foi medida e em que os limiares de completude do resultado (E4.9) foram calibrados. É a única população em que
    /// esses dois limiares significam alguma coisa: numa amostra sorteada eles passam por vacuidade.
    /// </summary>
    LargeNearDuplicateGroupMembers = 4
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
