namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Models;

/// <summary>
/// O quanto uma métrica se degradou ao embaralhar uma feature, com a DISPERSÃO entre as permutações.
///
/// <para>A permutação é estocástica: cada embaralhamento produz um número diferente. Publicar só a média
/// esconde isso e faz duas features com sobreposição total de intervalos parecerem ordenadas. O desvio é o
/// que permite ao leitor decidir se a diferença entre duas linhas do ranking significa alguma coisa.</para>
/// </summary>
/// <param name="Mean">Degradação média da métrica entre as permutações.</param>
/// <param name="StandardDeviation">Desvio-padrão da degradação entre as permutações.</param>
public sealed record MetricDelta(double Mean, double StandardDeviation);

/// <summary>
/// A importância de UM slot do vetor de features, antes da agregação por bloco. Um bloco categórico one-hot
/// ocupa muitos slots (<c>GenreEncoded.rock</c>, <c>GenreEncoded.pop</c>, ...), e todos apontam para a mesma
/// feature lógica — é o <see cref="Feature"/> já resolvido para o nome do bloco que permite reagrupá-los.
/// </summary>
/// <param name="Feature">Nome LÓGICO da feature a que o slot pertence.</param>
/// <param name="RSquaredDrop">Queda de R² ao embaralhar este slot.</param>
/// <param name="MeanAbsoluteErrorIncrease">Aumento do MAE, em pontos de popularidade, ao embaralhar o slot.</param>
public sealed record FeatureSlotImportance(
    string Feature,
    MetricDelta RSquaredDrop,
    MetricDelta MeanAbsoluteErrorIncrease);

/// <summary>
/// Uma linha do ranking de importância publicado: quanto o modelo PERDE quando aquela feature deixa de
/// informar. Números orientados para cima serem sempre "mais importante" — queda de R² e aumento de MAE.
///
/// <para>As duas métricas viajam juntas pelo mesmo motivo de <c>RegressionMetrics</c>: a queda de R² ordena,
/// mas é adimensional; o aumento de MAE diz o preço em PONTOS DE POPULARIDADE, que é o número que um leitor
/// consegue interpretar.</para>
/// </summary>
/// <param name="Feature">Nome lógico da feature (blocos one-hot já agregados sob o nome do bloco).</param>
/// <param name="SlotCount">Quantos slots do vetor foram agregados nesta linha (1 para feature escalar).</param>
/// <param name="RSquaredDrop">Queda de R² atribuída à feature — o critério de ordenação.</param>
/// <param name="MeanAbsoluteErrorIncrease">Aumento do MAE atribuído à feature, em pontos de popularidade.</param>
public sealed record FeatureImportance(
    string Feature,
    int SlotCount,
    MetricDelta RSquaredDrop,
    MetricDelta MeanAbsoluteErrorIncrease);

/// <summary>
/// Monta o ranking de importância a partir das medições por slot: agrega os slots de um mesmo bloco
/// categórico sob o nome do bloco e ordena por degradação decrescente.
///
/// <para>Vive no DOMÍNIO, e não na Infrastructure, pela mesma razão de <c>RegressionMetricsCalculator</c>: é
/// aritmética pura sobre números já medidos, então dá para testá-la com valores conhecidos, sem ML.NET. O que
/// a Infrastructure faz é medir; o que se faz com a medição é regra.</para>
///
/// <para><b>Como os slots são combinados.</b> As médias SOMAM: a degradação total atribuível ao bloco é a
/// soma das degradações de suas colunas. As dispersões somam EM QUADRATURA (raiz da soma das variâncias), e
/// não linearmente, porque as permutações de slots diferentes são sorteios independentes — somar desvios
/// diretamente inflaria a incerteza publicada.</para>
/// </summary>
public static class FeatureImportanceRanking
{
    /// <summary>
    /// Agrega os slots por feature lógica e devolve o ranking ordenado.
    /// </summary>
    public static IReadOnlyList<FeatureImportance> Build(IEnumerable<FeatureSlotImportance> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        IReadOnlyList<FeatureImportance> aggregated = slots
            .GroupBy(slot => slot.Feature, StringComparer.Ordinal)
            .Select(group => new FeatureImportance(
                group.Key,
                group.Count(),
                Combine(group.Select(slot => slot.RSquaredDrop)),
                Combine(group.Select(slot => slot.MeanAbsoluteErrorIncrease))))
            .ToArray();

        return Order(aggregated);
    }

    /// <summary>
    /// Ordena um ranking já agregado: maior queda de R² primeiro, com o aumento de MAE como desempate e o
    /// nome como critério final — sem o último, duas features empatadas trocariam de lugar entre execuções.
    /// </summary>
    public static IReadOnlyList<FeatureImportance> Order(IEnumerable<FeatureImportance> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        return features
            .OrderByDescending(feature => feature.RSquaredDrop.Mean)
            .ThenByDescending(feature => feature.MeanAbsoluteErrorIncrease.Mean)
            .ThenBy(feature => feature.Feature, StringComparer.Ordinal)
            .ToArray();
    }

    private static MetricDelta Combine(IEnumerable<MetricDelta> deltas)
    {
        double mean = 0;
        double variance = 0;

        foreach (MetricDelta delta in deltas)
        {
            mean += delta.Mean;
            variance += delta.StandardDeviation * delta.StandardDeviation;
        }

        return new MetricDelta(mean, Math.Sqrt(variance));
    }
}
