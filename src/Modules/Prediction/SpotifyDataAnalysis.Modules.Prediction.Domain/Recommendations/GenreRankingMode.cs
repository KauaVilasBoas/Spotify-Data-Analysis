namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Como o gênero da faixa-semente entra (ou não) no ranking de recomendação (E4.3, DP-C/DP-1). É o eixo de
/// relaxamento exposto no endpoint do E4.2: por default o gênero PESA no ranking (híbrido leve), mas o cliente pode
/// desligá-lo e voltar ao cosine puro do E4.1, ou pedir o corte duro por gênero.
///
/// <para>É uma Strategy de ranking, não uma dimensão do vetor: <see cref="SimilarityFeature"/> descreve o espaço
/// z-score do cosine (que não muda entre os modos); este enum escolhe o ESTÁGIO de gênero aplicado por cima dele.
/// A política que materializa cada modo é <see cref="GenreAffinityPolicy"/>.</para>
/// </summary>
public enum GenreRankingMode
{
    /// <summary>
    /// Gênero IGNORADO: o ranking é o cosine puro de audio-features do E4.1. É o relaxamento total da DP-C — o
    /// cliente pediu "só-áudio", e faixas de qualquer gênero concorrem sem bônus nem corte.
    /// </summary>
    Off = 0,

    /// <summary>
    /// Gênero como BOOST (default, DP-1): todas as candidatas concorrem por cosine, mas as do MESMO gênero da
    /// semente recebem um bônus no score. Preserva o cosine como eixo principal e usa o gênero como reforço/
    /// desempate — o "híbrido leve" da DP-C, sem matar a serendipidade de um bom vizinho de gênero próximo.
    /// </summary>
    Boost = 1,

    /// <summary>
    /// Gênero como FILTRO duro: só entram no top-N candidatas do MESMO gênero da semente. É o corte explícito para
    /// quem quer coerência de gênero garantida — frágil na cauda de gêneros raros, por isso é opção, não default.
    /// </summary>
    SameGenreOnly = 2
}
