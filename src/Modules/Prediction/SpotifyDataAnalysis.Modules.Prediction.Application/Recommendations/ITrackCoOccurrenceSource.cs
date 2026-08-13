namespace SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;

/// <summary>
/// Lê a similaridade colaborativa item-item (E4.6) da matriz de co-ocorrência pré-computada
/// (<c>prediction.track_cooccurrence</c>, materializada pelo passo batch — DP-3). Dá, para uma faixa-semente, os
/// vizinhos que mais co-ocorrem com ela em playlists reais (Pichl, E4.5), já com o Jaccard normalizado.
///
/// <para>Porta na Application; a implementação Dapper vive junto das outras leituras do recomendador. É o análogo
/// colaborativo do <see cref="ISimilarityFeatureSource"/>: aquele traz o "soa parecido", este o "aparece junto".</para>
/// </summary>
public interface ITrackCoOccurrenceSource
{
    /// <summary>
    /// Os vizinhos colaborativos da semente, em ordem decrescente de Jaccard, no máximo <paramref name="limit"/>.
    /// Vazio quando a faixa nunca co-ocorreu (cobertura parcial — o chamador cai para o content puro). Lê os dois
    /// lados do par canônico (a semente como id menor OU maior).
    /// </summary>
    Task<IReadOnlyList<CoOccurringTrack>> FindCoOccurringAsync(
        string seedTrackId, int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Um vizinho colaborativo: a faixa que co-ocorre com a semente, quantas playlists as duas compartilham e o
/// Jaccard normalizado que mede a força do laço.
/// </summary>
/// <param name="TrackId">Id da faixa vizinha.</param>
/// <param name="CoPlaylists">Em quantas playlists a vizinha e a semente aparecem juntas.</param>
/// <param name="Jaccard">Interseção/união das playlists das duas, em [0, 1] — o sinal colaborativo normalizado.</param>
public sealed record CoOccurringTrack(string TrackId, int CoPlaylists, double Jaccard);
