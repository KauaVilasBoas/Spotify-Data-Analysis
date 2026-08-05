using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Uma faixa no espaço de similaridade: sua identidade mais o vetor NORMALIZADO que a representa, mais a marca de
/// imputação. É a unidade que povoa o <see cref="SimilarityIndex"/> e a que uma varredura kNN compara com a
/// semente.
///
/// <para><see cref="TrackId"/> é guardado por VALOR (string), sem qualquer tipo do Catalog — a fronteira entre
/// módulos é respeitada aqui como no resto do Prediction. <see cref="IsImputed"/> viaja junto (DP-F): uma faixa
/// com features imputadas é candidata legítima, mas o consumidor (E4.2) precisa poder sinalizar que a
/// similaridade veio de um valor estimado, não medido — imputado nunca passa por medido em silêncio.</para>
/// </summary>
public sealed class TrackFeatureVector
{
    private TrackFeatureVector(string trackId, SimilarityFeatureVector vector, bool isImputed)
    {
        TrackId = trackId;
        Vector = vector;
        IsImputed = isImputed;
    }

    /// <summary>Identidade da faixa no Spotify, por valor (o Prediction não conhece tipos do Catalog).</summary>
    public string TrackId { get; }

    /// <summary>O vetor NORMALIZADO (z-score) da faixa — o ponto efetivamente comparado por cosine.</summary>
    public SimilarityFeatureVector Vector { get; }

    /// <summary>Se as features desta faixa foram imputadas (DP-F). Candidata sim, mas marcada para o consumidor.</summary>
    public bool IsImputed { get; }

    /// <summary>
    /// Cria a entrada do índice. Exige um id não-vazio: uma faixa sem identidade não pode ser recomendada nem
    /// autoexcluída (a autoexclusão compara ids), então entrar no índice sem id seria um defeito à espera.
    /// </summary>
    /// <exception cref="DomainException">Quando o id da faixa é nulo ou em branco.</exception>
    public static TrackFeatureVector Create(string trackId, SimilarityFeatureVector vector, bool isImputed)
    {
        ArgumentNullException.ThrowIfNull(vector);

        if (string.IsNullOrWhiteSpace(trackId))
            throw new DomainException("Uma entrada do índice de similaridade precisa de um trackId não vazio.");

        return new TrackFeatureVector(trackId, vector, isImputed);
    }
}
