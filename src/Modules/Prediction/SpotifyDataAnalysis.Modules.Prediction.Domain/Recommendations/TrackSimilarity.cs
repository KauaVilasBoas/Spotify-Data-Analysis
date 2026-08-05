namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Uma faixa vizinha e o quão parecida ela é com a semente: o par (identidade, score de cosseno) que o kNN
/// devolve. <see cref="IsImputed"/> ecoa a marca da faixa vizinha (DP-F) para o consumidor decidir se sinaliza.
/// </summary>
/// <param name="TrackId">Identidade da faixa vizinha, por valor.</param>
/// <param name="Similarity">Cosseno entre a semente e a vizinha, em [−1, 1] — quanto maior, mais parecida.</param>
/// <param name="IsImputed">Se as features da vizinha foram imputadas, não medidas.</param>
public sealed record TrackSimilarity(string TrackId, double Similarity, bool IsImputed);
