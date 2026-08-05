namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Quanto UMA feature contribuiu para a similaridade entre a semente e uma candidata (E4.2, explicabilidade). É a
/// parcela da dimensão na soma do cosseno: no espaço z-score, <c>cosine = Σ (ŝᵢ·ĉᵢ) / (‖ŝ‖·‖ĉ‖)</c>, e esta é
/// exatamente a i-ésima parcela dessa soma. Somar a <see cref="Contribution"/> de todas as dimensões reconstrói o
/// score — é por isso que "as features que mais aproximaram" (maiores contribuições positivas) NUNCA contradizem o
/// ranking: a explicação é a decomposição do próprio número que ordena, não um cálculo paralelo.
///
/// <para>A contribuição é o número do espaço NORMALIZADO (adimensional). Os valores <see cref="SeedValue"/> e
/// <see cref="CandidateValue"/> são os ORIGINAIS (unidades do catálogo), porque "energy contribuiu 0,11 (z-score)"
/// não diz nada ao usuário — o que ele lê é "energy da semente 0,82, da candidata 0,79" (risco do card).</para>
/// </summary>
/// <param name="Feature">A feature de áudio a que esta contribuição se refere.</param>
/// <param name="SeedValue">Valor ORIGINAL (cru) da feature na semente, nas unidades do catálogo.</param>
/// <param name="CandidateValue">Valor ORIGINAL (cru) da feature na candidata, nas unidades do catálogo.</param>
/// <param name="Contribution">Parcela desta dimensão na soma do cosseno normalizado — quanto maior, mais aproximou.</param>
public sealed record FeatureContribution(
    SimilarityFeature Feature,
    double SeedValue,
    double CandidateValue,
    double Contribution);
