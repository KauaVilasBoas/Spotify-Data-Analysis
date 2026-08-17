using System.Globalization;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

/// <summary>
/// O gate de qualidade do recomendador (E4.4): reprova o motor quando os proxies degradam. Os limiares foram
/// cravados DEPOIS da primeira medição sobre o catálogo real (DP-2) — a lição do E3.2, cujo gate de R² foi posto
/// antes de medir e passou por margem de 0,0019.
///
/// <para><b>O gate de coerência é de DOIS LADOS, e essa é a decisão de design que importa.</b> Um gate só de piso
/// ("coerência ≥ X") seria satisfeito subindo o peso do boost até ele virar filtro duro — premiaria exatamente a
/// falha que este card diagnosticou. Por isso a coerência é cobrada em três frentes: um piso absoluto, um GANHO
/// mínimo sobre o cosine puro (o boost precisa estar fazendo diferença) e um TETO de saturação (o boost não pode
/// zerar a diversidade de gênero do top-N). Peso de menos falha o ganho; peso demais falha a saturação.</para>
///
/// <para><b>O proxy 3 é o único guarda não circular.</b> Coerência de gênero medida sobre um ranking que boosta
/// gênero mede em parte o próprio boost; a proximidade de duplicatas não usa gênero para nada. Se a normalização
/// ou o cosseno quebrarem, é ele que cai primeiro — por isso o gate o cobra em recall, alcance e no cosseno médio
/// dos irmãos reencontrados.</para>
/// </summary>
public sealed class RecommenderQualityGate
{
    /// <summary>
    /// Piso absoluto da coerência sob o boost default. Medido: 0,5833 com peso 0,05 (amostra fixa de 300 sementes,
    /// top-10). O limiar deixa margem folgada porque o alvo é detectar COLAPSO (o espaço deixar de agrupar por
    /// gênero), não oscilação normal de amostra.
    /// </summary>
    public const double MinimumGenreCoherence = 0.40;

    /// <summary>
    /// Ganho mínimo da coerência do boost sobre o piso do cosine puro. Medido: 0,5833 / 0,1347 = 4,33x. O limiar de
    /// 2x reprova um boost que virou decorativo (peso baixo demais para desempatar coisa alguma).
    /// </summary>
    public const double MinimumCoherenceLiftOverCosineOnly = 2.0;

    /// <summary>
    /// Teto da saturação: fração de sementes cujo top-N inteiro é de um único gênero. Medido: 0,3100 com peso 0,05.
    /// Acima de 0,50 o "boost" já decide a lista para a MAIORIA das sementes, o que o torna indistinguível do filtro
    /// duro e apaga o eixo de relaxamento da DP-C. Com os números medidos, um peso de 0,08 já reprovaria aqui.
    /// </summary>
    public const double MaximumGenreSaturation = 0.50;

    /// <summary>
    /// Recall mínimo de duplicatas normalizado pelo teto alcançável. Medido: 0,6108 (top-10, gênero desligado).
    /// </summary>
    public const double MinimumDuplicateRecall = 0.45;

    /// <summary>Fração mínima de sementes com ao menos um irmão no top-K. Medido: 0,6745.</summary>
    public const double MinimumDuplicateHitRate = 0.50;

    /// <summary>
    /// Cosseno médio mínimo dos irmãos reencontrados. Medido: 0,998244 — duplicatas são praticamente idênticas no
    /// espaço de features, então este número só se move se a normalização ou o cosseno quebrarem. É o detector mais
    /// sensível do gate, e por isso o limiar é alto.
    /// </summary>
    public const double MinimumSiblingCosine = 0.98;

    /// <summary>
    /// Avalia os proxies contra os limiares. Recebe as DUAS medições de coerência (cosine puro e boost) porque o
    /// ganho só existe como comparação pareada — pedir só a medição boostada tornaria o gate incapaz de distinguir
    /// "o boost funciona" de "o catálogo é homogêneo".
    /// </summary>
    /// <param name="cosineOnly">Medição com o gênero desligado — o piso da comparação.</param>
    /// <param name="boosted">Medição com o boost no peso default de produção.</param>
    /// <param name="duplicates">Proxy 3, sempre medido com o gênero desligado.</param>
    public RecommenderQualityVerdict Evaluate(
        RecommenderQualityMeasurement cosineOnly,
        RecommenderQualityMeasurement boosted,
        DuplicateProximityProxy duplicates)
    {
        ArgumentNullException.ThrowIfNull(cosineOnly);
        ArgumentNullException.ThrowIfNull(boosted);
        ArgumentNullException.ThrowIfNull(duplicates);

        var failures = new List<string>();

        if (boosted.SelfExclusion.Violations > 0)
            failures.Add(Describe(
                "autoexclusão", boosted.SelfExclusion.Violations, 0, "violação(ões) — o valor aceitável é zero"));

        if (cosineOnly.SelfExclusion.Violations > 0)
            failures.Add(Describe(
                "autoexclusão (cosine puro)", cosineOnly.SelfExclusion.Violations, 0,
                "violação(ões) — o valor aceitável é zero"));

        if (boosted.GenreCoherence.MeanCoherence < MinimumGenreCoherence)
            failures.Add(Describe(
                "coerência de gênero", boosted.GenreCoherence.MeanCoherence, MinimumGenreCoherence, "abaixo do piso"));

        double lift = cosineOnly.GenreCoherence.MeanCoherence <= 0
            ? double.PositiveInfinity
            : boosted.GenreCoherence.MeanCoherence / cosineOnly.GenreCoherence.MeanCoherence;

        if (lift < MinimumCoherenceLiftOverCosineOnly)
            failures.Add(Describe(
                "ganho da coerência sobre o cosine puro", lift, MinimumCoherenceLiftOverCosineOnly,
                "o boost deixou de desempatar"));

        if (boosted.GenreCoherence.SaturationRate > MaximumGenreSaturation)
            failures.Add(Describe(
                "saturação de gênero", boosted.GenreCoherence.SaturationRate, MaximumGenreSaturation,
                "acima do teto — o boost está agindo como filtro disfarçado"));

        if (duplicates.MeanRecall < MinimumDuplicateRecall)
            failures.Add(Describe(
                "recall de duplicatas", duplicates.MeanRecall, MinimumDuplicateRecall, "abaixo do piso"));

        if (duplicates.HitRate < MinimumDuplicateHitRate)
            failures.Add(Describe(
                "alcance de duplicatas", duplicates.HitRate, MinimumDuplicateHitRate, "abaixo do piso"));

        if (duplicates.MeanSiblingCosine < MinimumSiblingCosine)
            failures.Add(Describe(
                "cosseno médio das duplicatas", duplicates.MeanSiblingCosine, MinimumSiblingCosine,
                "abaixo do piso — sinal de quebra na normalização ou no cosseno"));

        return new RecommenderQualityVerdict(failures.Count == 0, failures);
    }

    private static string Describe(string proxy, double measured, double threshold, string reason) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}: medido {1:0.0000}, limiar {2:0.0000} — {3}.",
            proxy, measured, threshold, reason);
}

/// <summary>
/// O veredito do gate: aprovado ou reprovado, com a lista do que falhou. As falhas viajam como texto pronto porque
/// o consumidor é humano — a mensagem de um teste vermelho ou a linha de um relatório —, e um código de erro
/// obrigaria quem lê a traduzir de volta o número que já estava medido.
/// </summary>
/// <param name="IsApproved">Se nenhum proxy violou seu limiar.</param>
/// <param name="Failures">Uma descrição por proxy reprovado, com valor medido e limiar.</param>
public sealed record RecommenderQualityVerdict(bool IsApproved, IReadOnlyList<string> Failures);
