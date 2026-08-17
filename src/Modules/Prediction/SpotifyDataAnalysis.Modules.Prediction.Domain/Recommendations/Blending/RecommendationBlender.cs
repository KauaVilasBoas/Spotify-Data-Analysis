using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Blending;

/// <summary>De onde veio o sinal que sustentou uma recomendação no blend (E4.6) — a transparência de cobertura parcial.</summary>
public enum RecommendationSignal
{
    /// <summary>Só o content-based (áudio+gênero): a faixa não tem co-ocorrência registrada.</summary>
    ContentOnly = 0,

    /// <summary>Só o colaborativo (co-ocorrência): a faixa não estava no top-N de áudio, entrou pelo "aparece junto".</summary>
    CollaborativeOnly = 1,

    /// <summary>Ambos: a faixa soa parecido E aparece junto — o sinal mais forte.</summary>
    Blended = 2
}

/// <summary>
/// Um candidato do content-based visto pelo blend: identidade, o cosseno de áudio (o eixo a normalizar) e a
/// vizinha rica original, para o representante blendado herdar a explicabilidade sem recalculá-la.
/// </summary>
/// <param name="TrackId">Id da faixa.</param>
/// <param name="ContentScore">O score content-based (cosseno de áudio; o híbrido de gênero fica no <see cref="Neighbor"/>).</param>
/// <param name="Neighbor">A vizinha rica do content-based, ou <c>null</c> quando a faixa só tem sinal colaborativo.</param>
public sealed record BlendContentCandidate(string TrackId, double ContentScore, ExplainedTrackSimilarity? Neighbor);

/// <summary>Um vizinho colaborativo visto pelo blend: identidade, quantas playlists compartilha e o Jaccard já normalizado.</summary>
/// <param name="TrackId">Id da faixa.</param>
/// <param name="CoPlaylists">Playlists compartilhadas com a semente.</param>
/// <param name="Jaccard">O sinal colaborativo normalizado, em [0, 1].</param>
public sealed record BlendCollaborativeCandidate(string TrackId, int CoPlaylists, double Jaccard);

/// <summary>
/// Uma recomendação já blendada: a faixa, o score final, as duas parcelas que o compõem e qual sinal a sustentou.
/// </summary>
/// <param name="TrackId">Id da faixa recomendada.</param>
/// <param name="FinalScore">O score que ordena o ranking blendado, em [0, 1].</param>
/// <param name="NormalizedContentScore">A parcela de content (cosseno normalizado ao conjunto), em [0, 1] — 0 se só colaborativo.</param>
/// <param name="Jaccard">A parcela colaborativa (Jaccard), em [0, 1] — 0 se só content.</param>
/// <param name="CoPlaylists">Playlists compartilhadas com a semente (0 se só content).</param>
/// <param name="Signal">Qual sinal sustentou a recomendação.</param>
/// <param name="Neighbor">A vizinha rica do content-based quando existe (explicabilidade de áudio/gênero); null se só colaborativo.</param>
public sealed record BlendedRecommendation(
    string TrackId,
    double FinalScore,
    double NormalizedContentScore,
    double Jaccard,
    int CoPlaylists,
    RecommendationSignal Signal,
    ExplainedTrackSimilarity? Neighbor);

/// <summary>
/// Combina o sinal content-based (E4.1–E4.3, "soa parecido") com o colaborativo item-item (E4.6, "aparece junto")
/// num ranking único e explicável — o coração do card. Serviço de domínio SEM ESTADO: recebe as duas listas de
/// candidatos e o peso, e devolve o ranking blendado; não abre conexão, não conhece SQL.
///
/// <para><b>A fórmula (DP-2):</b> <c>final = (1 − w)·contentNorm + w·jaccard</c>, ambas as parcelas em [0, 1]. O
/// <c>contentNorm</c> é o cosseno reescalado por MIN-MAX ao conjunto de candidatos content (o cosseno bruto vive
/// num intervalo estreito e alto no z-score — 0,95–0,99 —, e sem reescalar o content dominaria o blend por
/// construção, não por mérito). O <c>jaccard</c> já vem normalizado por popularidade da própria métrica.</para>
///
/// <para><b>Cobertura parcial (o que o card pede explicitamente):</b> o conjunto blendado é a UNIÃO dos dois lados.
/// Uma faixa só no content entra com <c>jaccard = 0</c> (<see cref="RecommendationSignal.ContentOnly"/>); uma faixa
/// só no colaborativo entra com <c>contentNorm = 0</c> (<see cref="RecommendationSignal.CollaborativeOnly"/>) —
/// ela pode nem ter vetor de áudio, e ainda assim é recomendável pelo "aparece junto". As que estão nos dois lados
/// são <see cref="RecommendationSignal.Blended"/>, o sinal mais forte.</para>
/// </summary>
public sealed class RecommendationBlender
{
    /// <summary>Peso default do sinal colaborativo (DP-2). Conservador: o content ainda pesa mais (0,65 vs 0,35).</summary>
    public const double DefaultCollaborativeWeight = 0.35;

    private readonly double _collaborativeWeight;

    /// <param name="collaborativeWeight">Peso <c>w</c> do colaborativo, em [0, 1]. 0 = content puro; 1 = colaborativo puro.</param>
    /// <exception cref="DomainException">Quando o peso está fora de [0, 1].</exception>
    public RecommendationBlender(double collaborativeWeight = DefaultCollaborativeWeight)
    {
        if (collaborativeWeight is < 0.0 or > 1.0)
            throw new DomainException(
                $"O peso do sinal colaborativo deve estar em [0, 1]. Recebido: {collaborativeWeight}.");

        _collaborativeWeight = collaborativeWeight;
    }

    /// <summary>
    /// Blenda os dois sinais e devolve as <paramref name="limit"/> recomendações de maior score final, em ordem
    /// decrescente. A semente (<paramref name="seedTrackId"/>) é autoexcluída dos dois lados — a co-ocorrência de
    /// uma faixa consigo mesma não é recomendação.
    /// </summary>
    public IReadOnlyList<BlendedRecommendation> Blend(
        string seedTrackId,
        IReadOnlyList<BlendContentCandidate> contentCandidates,
        IReadOnlyList<BlendCollaborativeCandidate> collaborativeCandidates,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(contentCandidates);
        ArgumentNullException.ThrowIfNull(collaborativeCandidates);

        if (limit <= 0)
            throw new DomainException($"O limite do blend deve ser positivo. Recebido: {limit}.");

        (double min, double max) = ContentScoreRange(contentCandidates, seedTrackId);

        var byTrack = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        foreach (BlendContentCandidate candidate in contentCandidates)
        {
            if (IsSeed(candidate.TrackId, seedTrackId))
                continue;

            Accumulator entry = GetOrAdd(byTrack, candidate.TrackId);
            entry.HasContent = true;
            entry.NormalizedContent = Rescale(candidate.ContentScore, min, max);
            entry.Neighbor = candidate.Neighbor;
        }

        foreach (BlendCollaborativeCandidate candidate in collaborativeCandidates)
        {
            if (IsSeed(candidate.TrackId, seedTrackId))
                continue;

            Accumulator entry = GetOrAdd(byTrack, candidate.TrackId);
            entry.HasCollaborative = true;
            entry.Jaccard = candidate.Jaccard;
            entry.CoPlaylists = candidate.CoPlaylists;
        }

        var blended = new List<BlendedRecommendation>(byTrack.Count);
        foreach ((string trackId, Accumulator entry) in byTrack)
        {
            double finalScore =
                ((1.0 - _collaborativeWeight) * entry.NormalizedContent) + (_collaborativeWeight * entry.Jaccard);

            blended.Add(new BlendedRecommendation(
                trackId,
                finalScore,
                entry.NormalizedContent,
                entry.Jaccard,
                entry.CoPlaylists,
                entry.Signal,
                entry.Neighbor));
        }

        // Ordenação estável e determinística: score final desc; empate por Jaccard desc (favorece o colaborativo
        // comprovado num empate) e, por fim, o track_id, para a mesma entrada sempre sair na mesma ordem.
        blended.Sort((left, right) =>
        {
            int byScore = right.FinalScore.CompareTo(left.FinalScore);
            if (byScore != 0)
                return byScore;

            int byJaccard = right.Jaccard.CompareTo(left.Jaccard);
            return byJaccard != 0 ? byJaccard : string.CompareOrdinal(left.TrackId, right.TrackId);
        });

        return blended.Count <= limit ? blended : blended.GetRange(0, limit);
    }

    /// <summary>
    /// O intervalo [min, max] do cosseno de áudio no conjunto de candidatos content, para o min-max. Ignora a
    /// semente. Quando há um único candidato (ou todos iguais), o intervalo é degenerado e <see cref="Rescale"/>
    /// devolve 1,0 — o candidato content é o "melhor do seu conjunto", que é o comportamento certo.
    /// </summary>
    private static (double Min, double Max) ContentScoreRange(
        IReadOnlyList<BlendContentCandidate> contentCandidates, string seedTrackId)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        foreach (BlendContentCandidate candidate in contentCandidates)
        {
            if (IsSeed(candidate.TrackId, seedTrackId))
                continue;

            if (candidate.ContentScore < min)
                min = candidate.ContentScore;
            if (candidate.ContentScore > max)
                max = candidate.ContentScore;
        }

        return (min, max);
    }

    private static double Rescale(double value, double min, double max)
    {
        if (double.IsInfinity(min) || double.IsInfinity(max))
            return 0.0; // não havia candidato content — a parcela é zero (faixa só-colaborativa).

        double range = max - min;
        if (range <= double.Epsilon)
            return 1.0; // todos os cossenos iguais: cada um é o melhor do seu conjunto.

        return (value - min) / range;
    }

    private static bool IsSeed(string trackId, string seedTrackId) =>
        string.Equals(trackId, seedTrackId, StringComparison.Ordinal);

    private static Accumulator GetOrAdd(Dictionary<string, Accumulator> byTrack, string trackId)
    {
        if (!byTrack.TryGetValue(trackId, out Accumulator? entry))
        {
            entry = new Accumulator();
            byTrack[trackId] = entry;
        }

        return entry;
    }

    private sealed class Accumulator
    {
        public bool HasContent { get; set; }
        public bool HasCollaborative { get; set; }
        public double NormalizedContent { get; set; }
        public double Jaccard { get; set; }
        public int CoPlaylists { get; set; }
        public ExplainedTrackSimilarity? Neighbor { get; set; }

        public RecommendationSignal Signal => (HasContent, HasCollaborative) switch
        {
            (true, true) => RecommendationSignal.Blended,
            (false, true) => RecommendationSignal.CollaborativeOnly,
            _ => RecommendationSignal.ContentOnly
        };
    }
}
