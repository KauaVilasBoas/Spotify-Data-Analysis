namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Um heap MÍNIMO de capacidade fixa que retém os N vizinhos de MAIOR similaridade durante a varredura. É a
/// estrutura da seleção parcial do kNN: manter a raiz no MENOR dos N retidos permite decidir em O(1) se um novo
/// candidato entra (maior que a raiz → troca) e substituí-lo em O(log N), sem nunca ordenar o catálogo inteiro.
///
/// <para>Detalhe interno de <see cref="SimilarityIndex"/>, não uma abstração de domínio própria. O critério de
/// ordem é a similaridade; o desempate por <c>trackId</c> (ordinal) torna o resultado determinístico quando dois
/// candidatos empatam no cosseno — sem isso, a ordem dependeria da varredura e o smoke test não seria repetível.</para>
/// </summary>
internal sealed class TopNeighborHeap
{
    private readonly int _capacity;
    private readonly List<TrackSimilarity> _heap;

    public TopNeighborHeap(int capacity)
    {
        _capacity = capacity;
        _heap = new List<TrackSimilarity>(capacity);
    }

    /// <summary>
    /// Oferece um candidato ao top-N. Enquanto há espaço, ele entra; cheio, só entra se for melhor que o pior
    /// retido (a raiz do heap mínimo), substituindo-o. O "pior" e o "melhor" seguem o mesmo critério de
    /// <see cref="IsBetterThan"/> (similaridade, com desempate estável por id).
    /// </summary>
    public void Offer(TrackSimilarity candidate)
    {
        if (_heap.Count < _capacity)
        {
            _heap.Add(candidate);
            SiftUp(_heap.Count - 1);
            return;
        }

        if (IsBetterThan(candidate, _heap[0]))
        {
            _heap[0] = candidate;
            SiftDown(0);
        }
    }

    /// <summary>Os retidos em ordem DECRESCENTE de similaridade — o top-N final, do mais parecido ao menos.</summary>
    public IReadOnlyList<TrackSimilarity> ToDescending()
    {
        var ordered = new List<TrackSimilarity>(_heap);

        ordered.Sort((left, right) => IsBetterThan(left, right) ? -1 : IsBetterThan(right, left) ? 1 : 0);

        return ordered;
    }

    /// <summary>
    /// Um vizinho é "melhor" se tem maior similaridade; empatou, vence o menor <c>trackId</c> (ordinal). O
    /// desempate serve só à REPETIBILIDADE do ranking — não é uma preferência de negócio, é o que impede que a
    /// ordem de dois empatados dependa da ordem em que a varredura os encontrou.
    /// </summary>
    private static bool IsBetterThan(TrackSimilarity candidate, TrackSimilarity reference)
    {
        if (candidate.Similarity > reference.Similarity)
            return true;

        if (candidate.Similarity < reference.Similarity)
            return false;

        return string.CompareOrdinal(candidate.TrackId, reference.TrackId) < 0;
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;

            // Heap MÍNIMO: o filho sobe enquanto for PIOR (menor) que o pai, mantendo o pior na raiz.
            if (IsBetterThan(_heap[index], _heap[parent]))
                break;

            Swap(index, parent);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        int count = _heap.Count;

        while (true)
        {
            int smallest = index;
            int left = (2 * index) + 1;
            int right = (2 * index) + 2;

            if (left < count && IsBetterThan(_heap[smallest], _heap[left]))
                smallest = left;

            if (right < count && IsBetterThan(_heap[smallest], _heap[right]))
                smallest = right;

            if (smallest == index)
                break;

            Swap(index, smallest);
            index = smallest;
        }
    }

    private void Swap(int first, int second) =>
        (_heap[first], _heap[second]) = (_heap[second], _heap[first]);
}
