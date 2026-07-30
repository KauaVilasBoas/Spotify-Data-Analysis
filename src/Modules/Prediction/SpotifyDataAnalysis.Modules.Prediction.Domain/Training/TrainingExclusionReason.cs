namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// Por que uma faixa do catálogo ficou de fora do dataset de treino. Cada motivo é contado separadamente nas
/// estatísticas do dataset: descarte silencioso é o que transforma "treinei com o catálogo" em "treinei com
/// quem casou com o dataset externo" sem ninguém perceber.
/// </summary>
public enum TrainingExclusionReason
{
    /// <summary>A faixa não tem popularidade — sem alvo, não há o que aprender.</summary>
    MissingPopularity = 1,

    /// <summary>A faixa não tem audio-features (jsonb nulo): nunca casou com o dataset externo.</summary>
    MissingAudioFeatures = 2,

    /// <summary>A faixa tem audio-features, mas falta alguma das grandezas numéricas exigidas.</summary>
    IncompleteAudioFeatures = 3,

    /// <summary>
    /// A faixa é elegível estruturalmente, mas suas features foram imputadas e a política em vigor
    /// (<see cref="ImputedFeaturePolicy.ExcludeImputed"/>) mantém o treino restrito ao que foi medido.
    /// </summary>
    ImputedAudioFeatures = 4
}
