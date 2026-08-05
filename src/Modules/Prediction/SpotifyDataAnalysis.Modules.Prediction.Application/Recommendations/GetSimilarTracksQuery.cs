using System.Diagnostics;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

/// <summary>
/// Diagnóstico interno do recomendador (E4.1): dada uma faixa-semente do catálogo, devolve as N faixas mais
/// parecidas por cosseno sobre features normalizadas, com a latência da varredura. É o ÚNICO ponto de
/// diagnóstico que o card permite — o endpoint público, a explicabilidade e o porquê rico são do E4.2. Serve
/// para o smoke test e para medir a latência p95 exigida pela DP-D.
/// </summary>
/// <param name="SeedTrackId">Identidade da faixa-semente no catálogo.</param>
/// <param name="TopN">Quantos vizinhos retornar (top-N).</param>
public sealed record GetSimilarTracksQuery(string SeedTrackId, int TopN) : IQuery<SimilarTracksResult>
{
    /// <summary>Número de vizinhos padrão quando o chamador não especifica.</summary>
    public const int DefaultTopN = 10;

    /// <summary>Teto de vizinhos, para um pedido absurdo não virar uma resposta gigante.</summary>
    public const int MaximumTopN = 100;
}

/// <summary>
/// O resultado do diagnóstico. <see cref="SeedFound"/> distingue "a semente está no índice" de "não está"
/// (inexistente ou sem features completas) — quando falso, <see cref="Neighbors"/> vem vazio e o consumidor sabe
/// que o problema é a semente, não a ausência de vizinhos. <see cref="ScanLatencyMs"/> é a latência da VARREDURA
/// (sem a montagem do índice, que é amortizada), que é o número que a DP-D compara com o gatilho de 300 ms.
/// </summary>
/// <param name="SeedTrackId">A semente pedida, ecoada.</param>
/// <param name="SeedFound">Se a semente está no índice (existe e tem as nove features).</param>
/// <param name="IndexedTrackCount">Quantas faixas o índice cobre — o tamanho do espaço varrido.</param>
/// <param name="ScanLatencyMs">Latência, em ms, só da varredura kNN desta consulta.</param>
/// <param name="Neighbors">As faixas vizinhas, em ordem decrescente de similaridade, sem a própria semente.</param>
public sealed record SimilarTracksResult(
    string SeedTrackId,
    bool SeedFound,
    int IndexedTrackCount,
    double ScanLatencyMs,
    IReadOnlyList<SimilarTrackItem> Neighbors);

/// <summary>Uma faixa vizinha no resultado do diagnóstico: identidade, score de cosseno e marca de imputação (DP-F).</summary>
/// <param name="TrackId">Identidade da faixa vizinha, por valor.</param>
/// <param name="Similarity">Cosseno entre a semente e a vizinha, em [−1, 1].</param>
/// <param name="IsImputed">Se as features da vizinha foram imputadas, não medidas.</param>
public sealed record SimilarTrackItem(string TrackId, double Similarity, bool IsImputed);

internal sealed class GetSimilarTracksQueryHandler
    : IQueryHandler<GetSimilarTracksQuery, SimilarTracksResult>
{
    private readonly ITrackSimilarityIndexProvider _indexProvider;

    public GetSimilarTracksQueryHandler(ITrackSimilarityIndexProvider indexProvider) =>
        _indexProvider = indexProvider;

    public async Task<SimilarTracksResult> HandleAsync(
        GetSimilarTracksQuery request, CancellationToken cancellationToken = default)
    {
        int topN = Math.Clamp(request.TopN, 1, GetSimilarTracksQuery.MaximumTopN);

        SimilarityIndex index = await _indexProvider.GetIndexAsync(cancellationToken);

        // Só a varredura é cronometrada: a montagem do índice é amortizada (uma vez), e é a latência POR
        // CONSULTA que a DP-D compara com o gatilho de 300 ms.
        long startTimestamp = Stopwatch.GetTimestamp();
        IReadOnlyList<TrackSimilarity>? neighbors = index.FindNearestTo(request.SeedTrackId, topN);
        double scanLatencyMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (neighbors is null)
            return new SimilarTracksResult(
                request.SeedTrackId, SeedFound: false, index.Count, scanLatencyMs, []);

        var items = new List<SimilarTrackItem>(neighbors.Count);
        foreach (TrackSimilarity neighbor in neighbors)
            items.Add(new SimilarTrackItem(neighbor.TrackId, neighbor.Similarity, neighbor.IsImputed));

        return new SimilarTracksResult(
            request.SeedTrackId, SeedFound: true, index.Count, scanLatencyMs, items);
    }
}
