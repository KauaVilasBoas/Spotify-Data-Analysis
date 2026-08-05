namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// As nove audio-features CONTÍNUAS que compõem o vetor de similaridade do recomendador content-based (E4.1,
/// DP-2). É deliberadamente a MESMA lista já validada no E3.1: montar a similaridade sobre um conjunto/ordem
/// diferente do pipeline de popularidade reabriria, aqui, o risco de skew que o E3 fechou por construção.
///
/// <para>A ORDEM importa e é canônica: o índice de cada membro é a posição da feature dentro do vetor. Os
/// parâmetros de normalização (μ/σ por coluna) e o próprio vetor de cada faixa são indexados por essa ordem, e
/// a semente e os candidatos leem a mesma ordem — é isso que garante que a i-ésima coordenada da semente é
/// comparada com a i-ésima do candidato, e não com outra.</para>
///
/// <para><c>Key</c>/<c>Mode</c>/<c>TimeSignature</c> ficam FORA (não são contínuas e não pagaram no E3.3);
/// <c>DurationMs</c>/<c>Explicit</c> ficam fora do vetor de SIMILARIDADE (são preditores de popularidade, não
/// eixos de semelhança sonora); gênero entra como filtro/boost do E4.3, não como dimensão do cosine.</para>
/// </summary>
public enum SimilarityFeature
{
    Danceability = 0,
    Energy = 1,
    Valence = 2,
    Tempo = 3,
    Acousticness = 4,
    Instrumentalness = 5,
    Liveness = 6,
    Speechiness = 7,
    Loudness = 8
}

/// <summary>
/// A lista canônica das <see cref="SimilarityFeature"/> na ordem do vetor. Existe para que "as nove features na
/// ordem certa" tenha uma ÚNICA fonte da verdade — leitura do índice, normalização e testes derivam daqui em
/// vez de repetir a sequência mágica, que é justamente como um skew de ordem se instala sem ninguém ver.
/// </summary>
public static class SimilarityFeatures
{
    /// <summary>As nove features contínuas na ordem canônica do vetor (índice = posição).</summary>
    public static readonly IReadOnlyList<SimilarityFeature> Ordered =
    [
        SimilarityFeature.Danceability,
        SimilarityFeature.Energy,
        SimilarityFeature.Valence,
        SimilarityFeature.Tempo,
        SimilarityFeature.Acousticness,
        SimilarityFeature.Instrumentalness,
        SimilarityFeature.Liveness,
        SimilarityFeature.Speechiness,
        SimilarityFeature.Loudness
    ];

    /// <summary>A dimensão do vetor de similaridade: nove coordenadas.</summary>
    public static int Dimension => Ordered.Count;
}
