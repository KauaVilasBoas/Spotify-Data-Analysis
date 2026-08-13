using System.Globalization;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

/// <summary>
/// Uma CONFIGURAÇÃO de ranking sob avaliação — o eixo que a calibração do E4.4 varre: cosine puro (o piso) ou
/// boost de gênero com um peso específico. Existe para que a varredura de pesos seja uma lista de valores, e não
/// um bloco de código duplicado por peso, e para que cada linha da tabela do relatório saia rotulada.
///
/// <para><b>Não é uma segunda política de gênero:</b> a regra continua sendo a <see cref="GenreAffinityPolicy"/>
/// do E4.3, construída aqui por <see cref="PolicyFor"/>. Esta classe só escolhe COM QUE PARÂMETROS a política do
/// produto é instanciada — medir com uma mecânica diferente da de produção mediria outro sistema.</para>
/// </summary>
public sealed record RecommenderEvaluationSetting
{
    private RecommenderEvaluationSetting(GenreRankingMode mode, double boostWeight, string label)
    {
        Mode = mode;
        BoostWeight = boostWeight;
        Label = label;
    }

    /// <summary>O modo de gênero pedido nesta configuração.</summary>
    public GenreRankingMode Mode { get; }

    /// <summary>O peso do boost desta configuração; irrelevante fora do modo boost.</summary>
    public double BoostWeight { get; }

    /// <summary>Rótulo curto da configuração para a tabela do relatório (ex.: <c>off</c>, <c>boost 0,050</c>).</summary>
    public string Label { get; }

    /// <summary>O piso da comparação: cosine puro do E4.1, sem o gênero pesar em nada.</summary>
    public static RecommenderEvaluationSetting CosineOnly() =>
        new(GenreRankingMode.Off, boostWeight: 0.0, label: "off");

    /// <summary>
    /// Boost de gênero com o peso informado — uma linha da varredura de calibração.
    /// </summary>
    /// <exception cref="DomainException">Quando o peso é negativo (a própria política do E4.3 já o proíbe).</exception>
    public static RecommenderEvaluationSetting BoostedBy(double boostWeight)
    {
        if (boostWeight < 0)
            throw new DomainException(
                $"Não faz sentido avaliar um peso de boost negativo (recebido: {boostWeight}).");

        string label = string.Format(CultureInfo.InvariantCulture, "boost {0:0.000}", boostWeight);

        return new RecommenderEvaluationSetting(GenreRankingMode.Boost, boostWeight, label);
    }

    /// <summary>
    /// Instancia a política de PRODUÇÃO para uma semente concreta sob esta configuração. O fallback gracioso do
    /// E4.3 (semente sem gênero utilizável cai no cosine puro) continua valendo aqui — a avaliação vê exatamente o
    /// que o endpoint veria.
    /// </summary>
    public GenreAffinityPolicy PolicyFor(string? seedGenre, bool seedGenreIsImputed) =>
        GenreAffinityPolicy.Create(Mode, seedGenre, seedGenreIsImputed, BoostWeight);
}
