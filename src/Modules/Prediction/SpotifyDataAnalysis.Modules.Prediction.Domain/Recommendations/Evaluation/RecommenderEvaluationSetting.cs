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
///
/// <para><b>Por que ela cresceu no E4.8:</b> os números do E4.4 foram medidos antes do dedup (E4.7) e do blend
/// (E4.6), os dois hoje no caminho do endpoint. Uma configuração que só dizia "qual peso de gênero" descreve um
/// sistema que não existe mais — e número sem a configuração que o produziu é o defeito central que o E4.8 veio
/// consertar. Agora ela carrega os QUATRO eixos do endpoint (<c>genreMode</c>, <c>dedupe</c>, <c>strategy</c>,
/// <c>blendWeight</c>), e o <see cref="Label"/> de cada linha do relatório os exibe inteiros.</para>
/// </summary>
public sealed record RecommenderEvaluationSetting
{
    private RecommenderEvaluationSetting(
        GenreRankingMode mode, double boostWeight, bool dedupe, double? blendWeight)
    {
        Mode = mode;
        BoostWeight = boostWeight;
        Dedupe = dedupe;
        BlendWeight = blendWeight;
    }

    /// <summary>O modo de gênero pedido nesta configuração.</summary>
    public GenreRankingMode Mode { get; }

    /// <summary>O peso do boost desta configuração; irrelevante fora do modo boost.</summary>
    public double BoostWeight { get; }

    /// <summary>Se o top-N medido passa pelo dedup de quase-duplicatas do E4.7 (<c>dedupe=true</c> é o default do endpoint).</summary>
    public bool Dedupe { get; }

    /// <summary>
    /// O peso do sinal colaborativo quando a estratégia é <c>blend</c> (E4.6), ou <see langword="null"/> no content
    /// puro. Um único campo em vez de um par "estratégia + peso" porque os dois estados possíveis são exatamente
    /// "sem blend" e "blend com peso w" — um enum à parte só criaria a combinação inválida <c>content</c> + peso.
    /// </summary>
    public double? BlendWeight { get; }

    /// <summary>Se esta configuração blenda o sinal colaborativo ao content-based.</summary>
    public bool IsBlended => BlendWeight is not null;

    /// <summary>
    /// Rótulo da configuração para a tabela do relatório, com os QUATRO eixos visíveis — ex.:
    /// <c>boost 0.050 | dedupe=on | content</c>. É o rótulo que impede um número de circular sem a configuração.
    /// </summary>
    public string Label => string.Format(
        CultureInfo.InvariantCulture,
        "{0} | dedupe={1} | {2}",
        Mode switch
        {
            GenreRankingMode.Off => "off",
            GenreRankingMode.SameGenreOnly => "same-genre",
            _ => string.Format(CultureInfo.InvariantCulture, "boost {0:0.000}", BoostWeight)
        },
        Dedupe ? "on" : "off",
        BlendWeight is double weight
            ? string.Format(CultureInfo.InvariantCulture, "blend {0:0.00}", weight)
            : "content");

    /// <summary>O piso da comparação: cosine puro do E4.1, sem o gênero pesar em nada.</summary>
    public static RecommenderEvaluationSetting CosineOnly() =>
        new(GenreRankingMode.Off, boostWeight: 0.0, dedupe: false, blendWeight: null);

    /// <summary>
    /// Boost de gênero com o peso informado — uma linha da varredura de calibração.
    /// </summary>
    /// <exception cref="DomainException">Quando o peso é negativo (a própria política do E4.3 já o proíbe).</exception>
    public static RecommenderEvaluationSetting BoostedBy(double boostWeight)
    {
        if (boostWeight < 0)
            throw new DomainException(
                $"Não faz sentido avaliar um peso de boost negativo (recebido: {boostWeight}).");

        return new RecommenderEvaluationSetting(
            GenreRankingMode.Boost, boostWeight, dedupe: false, blendWeight: null);
    }

    /// <summary>A mesma configuração, com o dedup do E4.7 ligado ou desligado no top-N medido.</summary>
    public RecommenderEvaluationSetting WithDedupe(bool dedupe = true) =>
        new(Mode, BoostWeight, dedupe, BlendWeight);

    /// <summary>
    /// A mesma configuração, agora blendando o sinal colaborativo (E4.6) com o peso informado.
    /// </summary>
    /// <exception cref="DomainException">Quando o peso sai de [0, 1] — a mesma faixa que o blender de produção exige.</exception>
    public RecommenderEvaluationSetting WithBlend(double blendWeight)
    {
        if (blendWeight is < 0.0 or > 1.0)
            throw new DomainException(
                $"O peso do sinal colaborativo avaliado deve estar em [0, 1]. Recebido: {blendWeight}.");

        return new RecommenderEvaluationSetting(Mode, BoostWeight, Dedupe, blendWeight);
    }

    /// <summary>
    /// Instancia a política de PRODUÇÃO para uma semente concreta sob esta configuração. O fallback gracioso do
    /// E4.3 (semente sem gênero utilizável cai no cosine puro) continua valendo aqui — a avaliação vê exatamente o
    /// que o endpoint veria.
    /// </summary>
    public GenreAffinityPolicy PolicyFor(string? seedGenre, bool seedGenreIsImputed) =>
        GenreAffinityPolicy.Create(Mode, seedGenre, seedGenreIsImputed, BoostWeight);
}
