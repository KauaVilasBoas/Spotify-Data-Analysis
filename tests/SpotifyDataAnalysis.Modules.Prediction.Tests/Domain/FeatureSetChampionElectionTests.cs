using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// A eleição do campeão do E3.3 como REGRA de negócio pura: dado o MAE medido de cada feature set, decide qual
/// publicar. Sem ML.NET — a lógica é aritmética sobre métricas, então é aqui que ela é provada, com números
/// escolhidos para cobrir cada ramo (ambos pagam, nenhum paga, só um paga, e a salvaguarda de menor MAE).
/// </summary>
public sealed class FeatureSetChampionElectionTests
{
    private const string Baseline = "baseline";
    private const string BlockA = "+A";
    private const string BlockB = "+B";
    private const string Combined = "A+B";

    private static RegressionMetrics Mae(double mae) => new(0.1, mae, mae + 1);

    private static FeatureSetChampionElection.Result Elect(
        double baseline, double a, double b, double ab)
    {
        FeatureSetChampionElection.Option Option(string label, double mae) => new(label, Mae(mae));

        return FeatureSetChampionElection.Elect(
            Option(Baseline, baseline),
            [Option(BlockA, a), Option(BlockB, b)],
            [Option(Baseline, baseline), Option(BlockA, a), Option(BlockB, b), Option(Combined, ab)],
            ResolveCombination);
    }

    /// <summary>Nomenclatura dos conjuntos, do lado do "pipeline" — a regra não a conhece.</summary>
    private static string ResolveCombination(IReadOnlyList<string> approved) => (
        approved.Contains(BlockA), approved.Contains(BlockB)) switch
    {
        (true, true) => Combined,
        (true, false) => BlockA,
        (false, true) => BlockB,
        _ => Baseline
    };

    [Fact]
    public void WhenBothBlocksPay_AndCombinationIsBest_ChampionIsTheCombination()
    {
        // base 15,168 → 2% = 14,865. A=14,6, B=14,7 pagam; A+B=14,4 é o melhor medido.
        FeatureSetChampionElection.Result result = Elect(15.168, 14.6, 14.7, 14.4);

        Assert.Equal(Combined, result.ChampionLabel);
        Assert.All(result.BlockGains, gain => Assert.True(gain.Pays));
    }

    [Fact]
    public void WhenNoBlockPays_ChampionStaysTheBaseline()
    {
        // Nenhum bloco reduz o MAE em 2%: a base permanece campeã — resultado honesto e válido do card.
        FeatureSetChampionElection.Result result = Elect(15.168, 15.10, 15.05, 15.00);

        Assert.Equal(Baseline, result.ChampionLabel);
        Assert.All(result.BlockGains, gain => Assert.False(gain.Pays));
    }

    [Fact]
    public void WhenOnlyOneBlockPays_ChampionIsThatBlock()
    {
        // A paga (14,5), B não (15,10). Campeão = +A, mesmo que A+B exista.
        FeatureSetChampionElection.Result result = Elect(15.168, 14.5, 15.10, 14.55);

        Assert.Equal(BlockA, result.ChampionLabel);
    }

    [Fact]
    public void WhenBothPayByRule_ButCombinationIsWorseThanASingleBlock_SafeguardPicksTheBestMeasured()
    {
        // Regra elegeria A+B (ambos pagam isolados: A=14,5, B=14,6), mas A+B=14,9 mede pior que +A=14,5. A
        // salvaguarda de menor MAE publica +A: a medição manda, não a soma dos veredictos.
        FeatureSetChampionElection.Result result = Elect(15.168, 14.5, 14.6, 14.9);

        Assert.Equal(BlockA, result.ChampionLabel);
    }

    [Fact]
    public void Rationale_NamesEachBlockVerdict_AndTheChampion()
    {
        FeatureSetChampionElection.Result result = Elect(15.168, 14.5, 15.10, 14.55);

        Assert.Contains("+A", result.Rationale);
        Assert.Contains("+B", result.Rationale);
        Assert.Contains("Campeão eleito", result.Rationale);
    }
}
