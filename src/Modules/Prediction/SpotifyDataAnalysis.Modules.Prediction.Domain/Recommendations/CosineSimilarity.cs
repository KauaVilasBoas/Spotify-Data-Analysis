using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Similaridade do cosseno entre dois vetores de features: o cosseno do ângulo entre eles, em [−1, 1]. Mede
/// SEMELHANÇA DE DIREÇÃO (o "perfil sonoro" da faixa), não de magnitude — que é o que se quer num recomendador
/// content-based. Aritmética pura no domínio: dá para prová-la contra números feitos à mão, sem banco e sem
/// framework, e é sobre ela que assentam os testes de sanidade (cosine(v, v) = 1; vetores idênticos → ~1).
/// </summary>
public static class CosineSimilarity
{
    /// <summary>
    /// Cosseno do ângulo entre <paramref name="left"/> e <paramref name="right"/>. Espera vetores no MESMO espaço
    /// (ambos normalizados, no uso do recomendador): comparar um cru com um normalizado daria um número plausível
    /// e sem sentido — o mesmo tipo de erro silencioso que a ausência de normalização causa.
    ///
    /// <para>Vetor de norma zero (a origem) não tem direção, então o cosseno é indefinido: devolvemos 0
    /// ("nenhuma semelhança de direção") em vez de NaN, para uma faixa degenerada não envenenar o ranking com um
    /// não-número. No espaço z-score isso só acontece com um vetor exatamente na média de todas as features.</para>
    /// </summary>
    /// <exception cref="DomainException">Quando as dimensões dos dois vetores divergem.</exception>
    public static double Between(SimilarityFeatureVector left, SimilarityFeatureVector right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Coordinates.Count != right.Coordinates.Count)
            throw new DomainException(
                "Cosine similarity exige vetores de mesma dimensão: " +
                $"{left.Coordinates.Count} contra {right.Coordinates.Count}.");

        if (left.Magnitude == 0 || right.Magnitude == 0)
            return 0;

        double dotProduct = 0;

        for (int index = 0; index < left.Coordinates.Count; index++)
            dotProduct += left.Coordinates[index] * right.Coordinates[index];

        return dotProduct / (left.Magnitude * right.Magnitude);
    }

    /// <summary>
    /// Decompõe o cosseno em UMA parcela por dimensão: o valor de <see cref="Between"/> é a SOMA das parcelas
    /// devolvidas aqui. Cada parcela é <c>(leftᵢ · rightᵢ) / (‖left‖·‖right‖)</c> — a contribuição da i-ésima
    /// coordenada ao score. É a base da explicabilidade do E4.2: "as features que mais aproximaram" são as de
    /// maior parcela, e como a soma reconstrói o próprio score, a explicação nunca diverge do ranking.
    ///
    /// <para>Devolve as parcelas na ORDEM canônica das coordenadas (índice = posição). Vetor de norma zero (a
    /// origem, sem direção) tem cosseno definido como 0 por <see cref="Between"/> — coerentemente, todas as
    /// parcelas são 0 aqui, para a decomposição continuar somando o mesmo score.</para>
    /// </summary>
    /// <exception cref="DomainException">Quando as dimensões dos dois vetores divergem.</exception>
    public static IReadOnlyList<double> ContributionsBetween(
        SimilarityFeatureVector left, SimilarityFeatureVector right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Coordinates.Count != right.Coordinates.Count)
            throw new DomainException(
                "Cosine similarity exige vetores de mesma dimensão: " +
                $"{left.Coordinates.Count} contra {right.Coordinates.Count}.");

        var contributions = new double[left.Coordinates.Count];

        if (left.Magnitude == 0 || right.Magnitude == 0)
            return contributions;

        double normProduct = left.Magnitude * right.Magnitude;

        for (int index = 0; index < left.Coordinates.Count; index++)
            contributions[index] = (left.Coordinates[index] * right.Coordinates[index]) / normProduct;

        return contributions;
    }
}
