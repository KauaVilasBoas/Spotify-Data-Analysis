using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

/// <summary>
/// A eleição do modelo campeão do E3.3 por MEDIÇÃO — regra de negócio pura, fora do pipeline do ML.NET para ser
/// testável com números conhecidos. Recebe as métricas de teste da base e de cada bloco (isolado e combinado) e
/// decide qual feature set publicar, aplicando a régua do card:
///
/// <list type="number">
///   <item>cada bloco só permanece se seu ganho ISOLADO de MAE sobre a base for ≥ 2%
///     (<see cref="FeatureBlockGainPolicy"/>);</item>
///   <item>o campeão eleito pela regra é a combinação dos blocos aprovados;</item>
///   <item>salvaguarda de honestidade: se essa combinação não tiver o MENOR MAE efetivamente medido, o campeão
///     passa a ser o conjunto de menor MAE — a medição manda, nunca a soma dos veredictos isolados.</item>
/// </list>
///
/// <para>O nome dos "blocos" é abstrato (<c>+A</c>, <c>+B</c>): esta classe não conhece Key/gênero, só métricas
/// e rótulos. Isso a mantém pura e reutilizável para futuros blocos (ex.: o Bloco C, se um dia entrar).</para>
/// </summary>
public static class FeatureSetChampionElection
{
    /// <summary>Uma opção de feature set medida no MESMO conjunto de teste: um rótulo e suas métricas.</summary>
    /// <param name="Label">Rótulo do conjunto (<c>baseline</c>, <c>+A</c>, <c>+B</c>, <c>A+B</c>).</param>
    /// <param name="Metrics">Métricas de teste do modelo treinado com este feature set.</param>
    public sealed record Option(string Label, RegressionMetrics Metrics);

    /// <summary>
    /// O resultado da eleição: o rótulo campeão, os ganhos medidos por bloco e uma justificativa legível.
    /// </summary>
    /// <param name="ChampionLabel">Rótulo do feature set eleito.</param>
    /// <param name="BlockGains">Ganho isolado de cada bloco sobre a base, com o veredito de "paga ou não".</param>
    /// <param name="Rationale">Justificativa bloco a bloco — inclui os que não pagaram.</param>
    public sealed record Result(
        string ChampionLabel,
        IReadOnlyList<FeatureBlockGain> BlockGains,
        string Rationale);

    /// <summary>
    /// Elege o campeão. <paramref name="blocks"/> mapeia o rótulo de cada bloco isolado (ex.: <c>+A</c>) ao
    /// rótulo do conjunto combinado que resulta de aprová-lo junto dos demais aprovados — mas, para manter a
    /// classe simples e alinhada ao card (dois blocos), a combinação é resolvida por
    /// <paramref name="combinationResolver"/>, que recebe os rótulos aprovados e devolve o rótulo do conjunto a
    /// publicar. Isso desacopla a REGRA (esta classe) da NOMENCLATURA dos conjuntos (o pipeline).
    /// </summary>
    /// <param name="baseline">Métricas da base (E3.2) — a âncora da comparação.</param>
    /// <param name="isolatedBlocks">Cada bloco medido ISOLADO sobre a base, na ordem de reporte.</param>
    /// <param name="allOptions">Todas as opções medidas (base + blocos + combinações), para a salvaguarda de menor MAE.</param>
    /// <param name="combinationResolver">Resolve, dado o conjunto de rótulos de blocos aprovados, o rótulo do conjunto a publicar.</param>
    /// <exception cref="DomainException">Quando não há opções, ou o resolvedor aponta um rótulo inexistente.</exception>
    public static Result Elect(
        Option baseline,
        IReadOnlyList<Option> isolatedBlocks,
        IReadOnlyList<Option> allOptions,
        Func<IReadOnlyList<string>, string> combinationResolver)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(isolatedBlocks);
        ArgumentNullException.ThrowIfNull(allOptions);
        ArgumentNullException.ThrowIfNull(combinationResolver);

        if (allOptions.Count == 0)
            throw new DomainException("A eleição do campeão precisa de ao menos uma opção medida.");

        FeatureBlockGain[] gains = isolatedBlocks
            .Select(block => FeatureBlockGainPolicy.Measure(block.Label, baseline.Metrics, block.Metrics))
            .ToArray();

        string[] approved = gains.Where(gain => gain.Pays).Select(gain => gain.BlockLabel).ToArray();

        // Os conjuntos ELEGÍVEIS são a base (sempre) e toda combinação formada apenas por blocos que pagaram
        // isoladamente — nunca um conjunto que carregue um bloco reprovado. Entre os elegíveis, a medição decide:
        // o campeão é o de MENOR MAE. Assim a régua dos 2% nunca é burlada (nada acima da base sem bloco
        // pagante), e ao mesmo tempo não se publica um A+B pior que um +A quando ambos pagam.
        Option champion = SubsetsOf(approved)
            .Select(combinationResolver)
            .Select(label => FindOption(allOptions, label))
            .Append(baseline)
            .OrderBy(option => option.Metrics.MeanAbsoluteError)
            .First();

        return new Result(champion.Label, gains, BuildRationale(gains, champion.Label));
    }

    /// <summary>Todos os subconjuntos (o conjunto potência) dos rótulos aprovados, inclusive o vazio.</summary>
    private static IEnumerable<IReadOnlyList<string>> SubsetsOf(IReadOnlyList<string> labels)
    {
        int total = 1 << labels.Count;

        for (int mask = 0; mask < total; mask++)
        {
            var subset = new List<string>(labels.Count);

            for (int bit = 0; bit < labels.Count; bit++)
            {
                if ((mask & (1 << bit)) != 0)
                    subset.Add(labels[bit]);
            }

            yield return subset;
        }
    }

    private static Option FindOption(IReadOnlyList<Option> options, string label) =>
        options.FirstOrDefault(option => option.Label == label)
        ?? throw new DomainException(
            $"O conjunto '{label}' derivado dos blocos aprovados não está entre as opções medidas — a eleição ficaria sem lastro.");

    private static string BuildRationale(IReadOnlyList<FeatureBlockGain> gains, string champion)
    {
        IEnumerable<string> verdicts = gains.Select(gain =>
        {
            string decision = gain.Pays ? "PAGA e permanece" : "não paga e é removido";
            string margin = gain.MaeGain.ToString(
                "P2", System.Globalization.CultureInfo.InvariantCulture);
            string required = FeatureBlockGainPolicy.RequiredMaeGain.ToString(
                "P2", System.Globalization.CultureInfo.InvariantCulture);

            return $"Bloco {gain.BlockLabel}: ganho de MAE {margin} (exigido {required}) — {decision}.";
        });

        return string.Join(" ", verdicts) + $" Campeão eleito: {champion}.";
    }
}
