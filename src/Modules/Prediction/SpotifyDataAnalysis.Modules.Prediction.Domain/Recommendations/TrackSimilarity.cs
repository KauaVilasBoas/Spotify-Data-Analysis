namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Uma faixa vizinha e o quão parecida ela é com a semente: o par (identidade, score) que o kNN devolve, agora com
/// o score DECOMPOSTO em suas duas parcelas (E4.3). <see cref="Similarity"/> é o score HÍBRIDO que ordena o ranking
/// — a soma do cosseno de áudio com o bônus de gênero; <see cref="CosineSimilarity"/> é só o cosseno (o eixo do
/// E4.1) e <see cref="GenreBonus"/> é o quanto o gênero somou. Quando o gênero não pesa (modo off/fallback), o
/// bônus é zero e o híbrido coincide com o cosseno — o comportamento do E4.1 continua exatamente ali dentro.
/// <see cref="IsImputed"/> ecoa a marca da faixa vizinha (DP-F) para o consumidor decidir se sinaliza.
/// </summary>
/// <param name="TrackId">Identidade da faixa vizinha, por valor.</param>
/// <param name="Similarity">Score HÍBRIDO que ordena: cosseno + bônus de gênero. Coincide com o cosseno quando o gênero não pesa.</param>
/// <param name="CosineSimilarity">Só o cosseno entre a semente e a vizinha, em [−1, 1] — o eixo do E4.1, sem o gênero.</param>
/// <param name="GenreBonus">Quanto o gênero somou ao score desta vizinha (0 quando não compartilha ou o gênero não pesa).</param>
/// <param name="IsImputed">Se as features da vizinha foram imputadas, não medidas.</param>
public sealed record TrackSimilarity(
    string TrackId,
    double Similarity,
    double CosineSimilarity,
    double GenreBonus,
    bool IsImputed);
