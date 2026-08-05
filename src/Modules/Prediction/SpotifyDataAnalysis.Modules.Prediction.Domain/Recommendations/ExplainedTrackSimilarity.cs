namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Uma vizinha com o "porquê rico" do E4.2: além da identidade e do score (o que o <see cref="TrackSimilarity"/>
/// já dava), a DECOMPOSIÇÃO do cosseno por dimensão — quanto cada feature contribuiu para aproximar a candidata da
/// semente. As contribuições vêm na ordem canônica das features; o consumidor escolhe as top-K a exibir. Somar
/// <see cref="FeatureContribution.Contribution"/> de todas reconstrói <see cref="Similarity"/>, então a explicação
/// nunca contradiz o ranking (é o mesmo número, esmiuçado).
/// </summary>
/// <param name="TrackId">Identidade da faixa vizinha, por valor.</param>
/// <param name="Similarity">Cosseno entre a semente e a vizinha, em [−1, 1] — o score que ordena.</param>
/// <param name="IsImputed">Se as features da vizinha foram imputadas, não medidas (DP-F).</param>
/// <param name="Contributions">A parcela de cada feature no score, na ordem canônica; com os valores originais.</param>
public sealed record ExplainedTrackSimilarity(
    string TrackId,
    double Similarity,
    bool IsImputed,
    IReadOnlyList<FeatureContribution> Contributions);
