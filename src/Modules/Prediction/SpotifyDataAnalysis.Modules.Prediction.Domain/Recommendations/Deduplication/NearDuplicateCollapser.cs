namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Deduplication;

/// <summary>
/// Um candidato do top-N no ponto de vista do dedup (E4.7): a vizinha rankeada mais os sinais que decidem se ela é
/// a MESMA música de outra e, em caso afirmativo, qual versão vira a representante. Ordem de campos escolhida para
/// o desempate do representante (DP-2), não para exibição.
/// </summary>
/// <param name="Neighbor">A vizinha original, com score e explicabilidade — nunca recalculada aqui.</param>
/// <param name="DuplicateKey">A chave "artista|título" reconstruída; vazia quando não há texto para casar.</param>
/// <param name="Popularity">Popularidade da faixa (0–100), quando conhecida — o 1º critério do representante.</param>
/// <param name="IsImputed">Se as features são imputadas — desempata a favor da medida.</param>
public sealed record DeduplicationCandidate(
    ExplainedTrackSimilarity Neighbor,
    RecommendationDuplicateKey DuplicateKey,
    int? Popularity,
    bool IsImputed)
{
    /// <summary>Id da faixa, atalho para o do vizinho.</summary>
    public string TrackId => Neighbor.TrackId;
}

/// <summary>
/// O resultado do dedup para uma posição do top-N: a vizinha REPRESENTANTE e quantas quase-duplicatas ela absorveu
/// (0 quando é única). A contagem alimenta a sinalização de transparência do response ("N versões equivalentes no
/// catálogo").
/// </summary>
/// <param name="Representative">A vizinha escolhida para representar o grupo.</param>
/// <param name="CollapsedDuplicateCount">Quantas OUTRAS versões foram colapsadas nesta (0 se nenhuma).</param>
public sealed record CollapsedRecommendation(ExplainedTrackSimilarity Representative, int CollapsedDuplicateCount);

/// <summary>
/// Colapsa quase-duplicatas do top-N do recomendador (E4.7): faixas que são a MESMA música com <c>track_id</c>s
/// diferentes (o catálogo tem ~37k pares assim, cosine ≈ 1,0, medido no E4.4) não devem ocupar mais de uma posição.
///
/// <para><b>É pós-processamento PURO</b> (o card manda): não toca no cosseno, na normalização nem no z-score do
/// E4.1. Recebe candidatas JÁ rankeadas, na ordem do score híbrido, e apenas decide quais fundir. A ordem de
/// entrada é preservada — o representante de um grupo ocupa a posição da PRIMEIRA versão vista (a de melhor score),
/// então o dedup nunca reordena o ranking, só remove repetição.</para>
///
/// <para><b>Dois critérios de "mesma música" (DP-1), em OU:</b></para>
/// <list type="number">
///   <item><b>Cosseno ≥ limiar altíssimo</b> (default 0,999) entre as versões — o critério primário e
///   independente de texto: duplicatas reais têm features praticamente idênticas. É o que pega o grosso dos ~37k
///   pares sem risco de fundir músicas diferentes (o cosseno de faixas distintas fica bem abaixo disso).</item>
///   <item><b>Mesma <see cref="RecommendationDuplicateKey"/></b> ("artista|título" normalizado) — reforço textual
///   para versões com features levemente distintas (remaster, edição) que o cosseno sozinho deixaria passar.</item>
/// </list>
///
/// <para><b>Representante (DP-2), determinístico:</b> maior <see cref="DeduplicationCandidate.Popularity"/>; empate
/// → a NÃO-imputada; novo empate → menor <c>track_id</c> (ordinal). Mesma entrada, mesmo representante — sempre. O
/// representante <b>herda a posição e o score da melhor versão do grupo</b> (a primeira na ordem de entrada), não
/// o seu próprio: fundir não pode rebaixar o grupo no ranking.</para>
/// </summary>
public sealed class NearDuplicateCollapser
{
    /// <summary>Limiar default de cosseno para declarar duas versões "a mesma música". Medido: irmãos ~0,9982 no E4.4.</summary>
    public const double DefaultCosineThreshold = 0.999;

    private readonly double _cosineThreshold;
    private readonly Func<string, string, double> _pairwiseCosine;

    /// <summary>
    /// </summary>
    /// <param name="pairwiseCosine">
    /// Função que devolve o cosseno entre os vetores NORMALIZADOS de dois <c>track_id</c>s do índice. Injetada para
    /// o colapsador não precisar conhecer a estrutura do índice — recebe só a capacidade de comparar duas faixas.
    /// </param>
    /// <param name="cosineThreshold">Limiar de cosseno; abaixo dele, só a chave textual funde.</param>
    public NearDuplicateCollapser(
        Func<string, string, double> pairwiseCosine, double cosineThreshold = DefaultCosineThreshold)
    {
        ArgumentNullException.ThrowIfNull(pairwiseCosine);

        _pairwiseCosine = pairwiseCosine;
        _cosineThreshold = cosineThreshold;
    }

    /// <summary>
    /// Colapsa as <paramref name="candidates"/> e devolve até <paramref name="limit"/> recomendações DISTINTAS, na
    /// ordem original. Passe MAIS candidatas que o <paramref name="limit"/> (over-fetch): sem folga, colapsar
    /// reduziria o resultado abaixo do limite, e o card exige entregar <c>limit</c> itens distintos.
    /// </summary>
    /// <param name="candidates">Candidatas já rankeadas por score decrescente (a semente já excluída).</param>
    /// <param name="limit">Quantos itens distintos entregar.</param>
    public IReadOnlyList<CollapsedRecommendation> Collapse(
        IReadOnlyList<DeduplicationCandidate> candidates, int limit)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var groups = new List<Group>();
        var byKey = new Dictionary<string, Group>(StringComparer.Ordinal);

        foreach (DeduplicationCandidate candidate in candidates)
        {
            Group? target = FindGroupFor(candidate, groups, byKey);

            if (target is null)
            {
                var group = new Group(candidate);
                groups.Add(group);

                if (!candidate.DuplicateKey.IsEmpty)
                    byKey[candidate.DuplicateKey.Value] = group;

                // Já temos itens distintos suficientes: para de abrir grupos novos. Grupos já abertos ainda
                // absorvem duplicatas das candidatas restantes (a contagem de colapsadas fica correta), mas o
                // resultado nunca passa de `limit`.
                if (groups.Count >= limit)
                    break;
            }
            else
            {
                target.Absorb(candidate);
            }
        }

        var result = new List<CollapsedRecommendation>(Math.Min(groups.Count, limit));
        foreach (Group group in groups)
        {
            result.Add(new CollapsedRecommendation(group.Representative.Neighbor, group.CollapsedCount));
            if (result.Count >= limit)
                break;
        }

        return result;
    }

    /// <summary>
    /// Acha o grupo ao qual a candidata pertence, ou <see langword="null"/> se ela abre um grupo novo. Tenta
    /// primeiro a chave textual (O(1)); se não houver, varre os grupos abertos comparando cosseno com o
    /// representante de cada um — barato porque o número de grupos é ≤ over-fetch (dezenas), não o catálogo.
    /// </summary>
    private Group? FindGroupFor(
        DeduplicationCandidate candidate, List<Group> groups, Dictionary<string, Group> byKey)
    {
        if (!candidate.DuplicateKey.IsEmpty
            && byKey.TryGetValue(candidate.DuplicateKey.Value, out Group? keyed))
        {
            return keyed;
        }

        foreach (Group group in groups)
        {
            if (_pairwiseCosine(candidate.TrackId, group.Representative.TrackId) >= _cosineThreshold)
                return group;
        }

        return null;
    }

    /// <summary>
    /// Um grupo de quase-duplicatas em construção. Guarda a PRIMEIRA candidata (que fixa a posição/score do grupo
    /// no ranking) e o REPRESENTANTE corrente (que pode mudar conforme a DP-2), sem nunca reordenar.
    /// </summary>
    private sealed class Group
    {
        private readonly DeduplicationCandidate _positionAnchor;

        public Group(DeduplicationCandidate first)
        {
            _positionAnchor = first;
            Representative = first;
        }

        /// <summary>A candidata escolhida pela DP-2 — mas com a explicabilidade da âncora (ver <see cref="Neighbor"/>).</summary>
        public DeduplicationCandidate Representative { get; private set; }

        /// <summary>Quantas OUTRAS versões o grupo absorveu.</summary>
        public int CollapsedCount { get; private set; }

        /// <summary>
        /// A vizinha exibida: identidade/atributos do representante escolhido pela DP-2, mas SCORE e explicabilidade
        /// da âncora de posição — fundir não rebaixa o grupo, e o "porquê rico" mostrado é o da melhor versão.
        /// </summary>
        public ExplainedTrackSimilarity Neighbor => Representative.TrackId == _positionAnchor.TrackId
            ? _positionAnchor.Neighbor
            : _positionAnchor.Neighbor with
            {
                TrackId = Representative.TrackId,
                IsImputed = Representative.IsImputed
            };

        public void Absorb(DeduplicationCandidate candidate)
        {
            CollapsedCount++;

            if (IsBetterRepresentative(candidate, Representative))
                Representative = candidate;
        }

        /// <summary>DP-2: maior popularity; empate → não-imputada; novo empate → menor track_id (ordinal).</summary>
        private static bool IsBetterRepresentative(DeduplicationCandidate candidate, DeduplicationCandidate current)
        {
            int candidatePopularity = candidate.Popularity ?? -1;
            int currentPopularity = current.Popularity ?? -1;

            if (candidatePopularity != currentPopularity)
                return candidatePopularity > currentPopularity;

            if (candidate.IsImputed != current.IsImputed)
                return !candidate.IsImputed;

            return string.CompareOrdinal(candidate.TrackId, current.TrackId) < 0;
        }
    }
}
