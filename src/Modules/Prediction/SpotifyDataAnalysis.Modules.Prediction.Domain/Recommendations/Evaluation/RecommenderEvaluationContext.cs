using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Blending;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Deduplication;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

/// <summary>
/// Os insumos que o top-N de PRODUÇÃO consome além do índice de similaridade: a chave de dedup e a popularidade de
/// cada faixa (E4.7) e o sinal colaborativo de cada semente (E4.6). Sem eles, a avaliação só consegue medir o
/// ranking cru do E4.1/E4.3 — que é exatamente o que o E4.4 mediu e o que deixou de ser o comportamento default.
///
/// <para><b>Por que é um dado de entrada, e não uma porta:</b> o avaliador é um serviço de domínio sem estado; abrir
/// conexão ou conhecer SQL aqui inverteria a dependência. O harness carrega tudo UMA vez e passa pronto — o mesmo
/// contrato do <see cref="RecommenderEvaluationSample"/>.</para>
///
/// <para><b>Custo:</b> a chave de dedup é pré-computada por faixa (a normalização é cara e não depende da semente),
/// e a popularidade é um <c>int?</c>. Nome e artista crus NÃO são retidos — só a chave derivada deles —, para a
/// avaliação não virar uma segunda cópia do catálogo em memória.</para>
/// </summary>
/// <param name="TrackAttributes">Por <c>track_id</c>: a chave "artista|título" e a popularidade que a DP-2 do dedup usa.</param>
/// <param name="CollaborativeBySeed">Por semente: os vizinhos colaborativos já lidos da matriz de co-ocorrência.</param>
public sealed record RecommenderEvaluationContext(
    IReadOnlyDictionary<string, EvaluationTrackAttributes> TrackAttributes,
    IReadOnlyDictionary<string, IReadOnlyList<BlendCollaborativeCandidate>> CollaborativeBySeed)
{
    private static readonly Dictionary<string, EvaluationTrackAttributes> NoAttributes =
        new(StringComparer.Ordinal);

    private static readonly Dictionary<string, IReadOnlyList<BlendCollaborativeCandidate>> NoCollaborative =
        new(StringComparer.Ordinal);

    /// <summary>
    /// O contexto vazio — o que basta para medir o ranking cru (nem dedup nem blend). É o default das sobrecargas
    /// de medição, e é o que mantém os testes de instrumento do E4.4 rodando sem banco.
    /// </summary>
    public static RecommenderEvaluationContext Empty { get; } = new(NoAttributes, NoCollaborative);

    /// <summary>
    /// Os atributos de dedup de uma faixa. Faixa desconhecida devolve chave vazia e popularidade nula — que é o
    /// MESMO que o handler de produção faz quando o metadata não traz a linha, e não um caso especial da avaliação.
    /// </summary>
    public EvaluationTrackAttributes AttributesOf(string trackId) =>
        TrackAttributes.TryGetValue(trackId, out EvaluationTrackAttributes? attributes)
            ? attributes
            : EvaluationTrackAttributes.Unknown;

    /// <summary>Os vizinhos colaborativos da semente; lista vazia significa cobertura zero (o blend cai no content).</summary>
    public IReadOnlyList<BlendCollaborativeCandidate> CollaborativeFor(string seedTrackId) =>
        CollaborativeBySeed.TryGetValue(seedTrackId, out IReadOnlyList<BlendCollaborativeCandidate>? candidates)
            ? candidates
            : [];
}

/// <summary>
/// O que o dedup do E4.7 precisa saber sobre uma faixa além do vetor: a chave "artista|título" que reconhece a
/// mesma música e a popularidade que decide o representante (DP-2).
/// </summary>
/// <param name="DuplicateKey">A chave textual normalizada, já pré-computada a partir de nome e artista.</param>
/// <param name="Popularity">Popularidade 0–100, ou <see langword="null"/> quando o catálogo não a conhece.</param>
public sealed record EvaluationTrackAttributes(RecommendationDuplicateKey DuplicateKey, int? Popularity)
{
    /// <summary>Faixa sem metadata: sem chave textual e sem popularidade — só o cosseno pode fundi-la.</summary>
    public static EvaluationTrackAttributes Unknown { get; } =
        new(RecommendationDuplicateKey.From(null, null), Popularity: null);
}
