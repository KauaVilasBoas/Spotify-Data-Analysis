using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Os parâmetros da padronização z-score (DP-1 do E4.1): a média (μ) e o desvio-padrão (σ) de CADA feature,
/// aprendidos uma única vez sobre o catálogo elegível. Transformar um vetor cru vira <c>(x − μ) / σ</c> coordenada
/// a coordenada — e é ESTE objeto, calculado uma vez e fixado junto do índice, que garante que a semente e os
/// candidatos passam pela MESMA transformação. Aplicar normalizações diferentes nos dois lados corromperia a
/// similaridade do mesmo jeito que o skew treino/inferência corrompe o E3.5.
///
/// <para><b>z-score, não min-max (DP-1):</b> as escalas são heterogêneas de fato (Tempo ~0–243, Loudness
/// ~−50–5, contra features 0–1) e min-max é ancorado nos extremos, ficando refém de outliers; a padronização se
/// apoia em μ/σ, mais robustos à cauda de Tempo/Loudness. Sem normalização, o cosine é dominado por Tempo e
/// Loudness e a similaridade vira lixo — o teste de dominância prova os dois lados disso.</para>
/// </summary>
public sealed class FeatureNormalizationParameters
{
    /// <summary>
    /// Desvio-padrão mínimo efetivo. Uma feature praticamente constante tem σ≈0, e dividir por ele estouraria a
    /// coordenada (ou geraria NaN) transformando ruído numérico em sinal dominante. Tratamos σ abaixo deste piso
    /// como "sem variância": a coordenada centrada é dividida por 1, colapsando para ~0 — o que é honesto, já que
    /// uma feature sem variância não distingue faixa nenhuma.
    /// </summary>
    public const double MinimumStandardDeviation = 1e-9;

    private readonly double[] _means;
    private readonly double[] _standardDeviations;

    private FeatureNormalizationParameters(double[] means, double[] standardDeviations)
    {
        _means = means;
        _standardDeviations = standardDeviations;
    }

    /// <summary>Média aprendida de cada feature, na ordem de <see cref="SimilarityFeatures.Ordered"/>.</summary>
    public IReadOnlyList<double> Means => _means;

    /// <summary>Desvio-padrão aprendido (já com o piso aplicado), na ordem canônica.</summary>
    public IReadOnlyList<double> StandardDeviations => _standardDeviations;

    /// <summary>
    /// Aprende μ e σ (populacional) de cada coluna a partir dos vetores CRUS do catálogo elegível. É uma passada
    /// única sobre o conjunto — os mesmos vetores que vão para o índice —, para o parâmetro e o índice nunca
    /// divergirem. σ recebe o piso de <see cref="MinimumStandardDeviation"/> por coluna.
    /// </summary>
    /// <exception cref="DomainException">Quando o conjunto de aprendizado está vazio.</exception>
    public static FeatureNormalizationParameters LearnFrom(IReadOnlyCollection<SimilarityFeatureVector> rawVectors)
    {
        ArgumentNullException.ThrowIfNull(rawVectors);

        if (rawVectors.Count == 0)
            throw new DomainException(
                "Não dá para aprender parâmetros de normalização sobre um catálogo vazio: sem faixas elegíveis, " +
                "não há μ/σ para fixar.");

        int dimension = SimilarityFeatures.Dimension;
        var means = new double[dimension];
        var standardDeviations = new double[dimension];

        foreach (SimilarityFeatureVector vector in rawVectors)
            for (int feature = 0; feature < dimension; feature++)
                means[feature] += vector.Coordinates[feature];

        for (int feature = 0; feature < dimension; feature++)
            means[feature] /= rawVectors.Count;

        foreach (SimilarityFeatureVector vector in rawVectors)
            for (int feature = 0; feature < dimension; feature++)
            {
                double deviation = vector.Coordinates[feature] - means[feature];
                standardDeviations[feature] += deviation * deviation;
            }

        for (int feature = 0; feature < dimension; feature++)
        {
            double variance = standardDeviations[feature] / rawVectors.Count;
            double standardDeviation = Math.Sqrt(variance);

            standardDeviations[feature] = standardDeviation < MinimumStandardDeviation
                ? 1.0
                : standardDeviation;
        }

        return new FeatureNormalizationParameters(means, standardDeviations);
    }

    /// <summary>
    /// Reconstrói os parâmetros a partir de μ/σ já conhecidos — útil para testes e para uma futura persistência
    /// dos parâmetros junto de um índice pré-computado (E4 fase 2). Aplica o mesmo piso de σ do aprendizado, para
    /// um σ inválido não escapar por este caminho.
    /// </summary>
    /// <exception cref="DomainException">Quando a dimensão de μ ou σ diverge da canônica.</exception>
    public static FeatureNormalizationParameters FromKnownStatistics(
        IReadOnlyList<double> means, IReadOnlyList<double> standardDeviations)
    {
        ArgumentNullException.ThrowIfNull(means);
        ArgumentNullException.ThrowIfNull(standardDeviations);

        if (means.Count != SimilarityFeatures.Dimension || standardDeviations.Count != SimilarityFeatures.Dimension)
            throw new DomainException(
                $"μ e σ devem ter {SimilarityFeatures.Dimension} entradas, uma por feature contínua. " +
                $"Recebido: {means.Count} médias e {standardDeviations.Count} desvios.");

        var flooredStandardDeviations = new double[standardDeviations.Count];

        for (int feature = 0; feature < standardDeviations.Count; feature++)
            flooredStandardDeviations[feature] = standardDeviations[feature] < MinimumStandardDeviation
                ? 1.0
                : standardDeviations[feature];

        return new FeatureNormalizationParameters(means.ToArray(), flooredStandardDeviations);
    }

    /// <summary>
    /// Padroniza um vetor CRU para o espaço z-score: cada coordenada vira <c>(x − μ) / σ</c> da sua feature. É a
    /// ÚNICA transformação de normalização do recomendador, aplicada identicamente à semente e aos candidatos.
    /// </summary>
    public SimilarityFeatureVector Normalize(SimilarityFeatureVector rawVector)
    {
        ArgumentNullException.ThrowIfNull(rawVector);

        int dimension = SimilarityFeatures.Dimension;
        var normalized = new double[dimension];

        for (int feature = 0; feature < dimension; feature++)
            normalized[feature] = (rawVector.Coordinates[feature] - _means[feature]) / _standardDeviations[feature];

        return SimilarityFeatureVector.Create(normalized);
    }
}
