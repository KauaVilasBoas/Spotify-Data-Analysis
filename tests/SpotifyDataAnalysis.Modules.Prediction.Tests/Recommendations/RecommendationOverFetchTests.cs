using SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// A aritmética do over-fetch adaptativo (E4.9), isolada de índice, catálogo e banco: janelas por rodada, teto
/// absoluto, critério de parada e o que o laço cobra de quem o chama.
///
/// <para><b>Por que testar o laço aqui, e não só pelo endpoint:</b> ele é a ÚNICA cópia da regra — handler de
/// produção, avaliador de qualidade e harness passam por ele. Um teste que só exercita o endpoint deixaria o
/// comportamento do avaliador implícito, que é exatamente a divergência que o E4.10 pagou para corrigir.</para>
/// </summary>
public sealed class RecommendationOverFetchTests
{
    [Theory]
    [InlineData(10, 30)]
    [InlineData(20, 60)]
    [InlineData(1, 10)]
    [InlineData(3, 10)]
    public void Primeira_rodada_mantem_a_formula_do_e4_11(int limit, int expected)
    {
        Assert.Equal(expected, RecommendationOverFetch.CountFor(limit));
        Assert.Equal(expected, RecommendationOverFetch.CountForRound(limit, round: 1));
    }

    [Fact]
    public void Janela_dobra_por_rodada()
    {
        Assert.Equal(30, RecommendationOverFetch.CountForRound(limit: 10, round: 1));
        Assert.Equal(60, RecommendationOverFetch.CountForRound(limit: 10, round: 2));
        Assert.Equal(120, RecommendationOverFetch.CountForRound(limit: 10, round: 3));
        Assert.Equal(120, RecommendationOverFetch.MaximumCountFor(limit: 10));
    }

    /// <summary>
    /// O teto ABSOLUTO de candidatas (DP-1) existe para que o custo por requisição seja limitado por uma constante, e
    /// não pelo produto <c>limit × fator × 2^rodadas</c>. No maior <c>limit</c> aceito pelo endpoint (50) a última
    /// rodada bate exatamente no teto; nada pode passar dele.
    /// </summary>
    [Fact]
    public void Nenhuma_rodada_passa_do_teto_absoluto_de_candidatas()
    {
        for (int limit = 1; limit <= 50; limit++)
        {
            for (int round = 1; round <= RecommendationOverFetch.MaximumRounds; round++)
            {
                Assert.True(
                    RecommendationOverFetch.CountForRound(limit, round) <= RecommendationOverFetch.MaximumCandidates,
                    $"limit={limit}, rodada={round} pediu " +
                    $"{RecommendationOverFetch.CountForRound(limit, round)} candidatas, acima do teto " +
                    $"{RecommendationOverFetch.MaximumCandidates}.");
            }
        }

        Assert.Equal(RecommendationOverFetch.MaximumCandidates, RecommendationOverFetch.MaximumCountFor(limit: 50));
    }

    [Fact]
    public void Rodada_fora_da_faixa_e_rejeitada()
    {
        Assert.Throws<DomainException>(() => RecommendationOverFetch.CountForRound(limit: 10, round: 0));
        Assert.Throws<DomainException>(
            () => RecommendationOverFetch.CountForRound(
                limit: 10, round: RecommendationOverFetch.MaximumRounds + 1));
    }

    /// <summary>
    /// Rodada extra NÃO é especulativa: se a primeira janela já entrega o pedido, o pós-processamento roda uma única
    /// vez. Num host de free tier onde cada rodada custa CPU, isso é o que mantém o caminho saudável no preço de antes
    /// do E4.9.
    /// </summary>
    [Fact]
    public void Resultado_completo_na_primeira_rodada_nao_pede_segunda()
    {
        var windows = new List<int>();

        RecommendationOverFetchOutcome<int> outcome = RecommendationOverFetch.Resolve(
            limit: 10,
            availableCandidates: 120,
            window =>
            {
                windows.Add(window);
                return 10;
            },
            resultCount: count => count);

        Assert.Equal([30], windows);
        Assert.Equal(1, outcome.RoundsUsed);
        Assert.Equal(30, outcome.CandidatesConsidered);
        Assert.Equal(10, outcome.Result);
    }

    [Fact]
    public void Resultado_curto_dobra_a_janela_e_para_quando_alcanca_o_pedido()
    {
        var windows = new List<int>();

        RecommendationOverFetchOutcome<int> outcome = RecommendationOverFetch.Resolve(
            limit: 10,
            availableCandidates: 120,
            window =>
            {
                windows.Add(window);
                return window >= 60 ? 10 : 4;
            },
            resultCount: count => count);

        Assert.Equal([30, 60], windows);
        Assert.Equal(2, outcome.RoundsUsed);
        Assert.Equal(60, outcome.CandidatesConsidered);
        Assert.Equal(10, outcome.Result);
    }

    /// <summary>
    /// A semente patológica do card: nada que se faça produz K. O laço tem de parar no teto de rodadas e devolver o
    /// que existe — nunca girar sem fim nem varrer o catálogo atrás de um K que o catálogo não tem.
    /// </summary>
    [Fact]
    public void Semente_patologica_para_no_teto_de_rodadas_e_devolve_o_que_existe()
    {
        var windows = new List<int>();

        RecommendationOverFetchOutcome<int> outcome = RecommendationOverFetch.Resolve(
            limit: 10,
            availableCandidates: 1_000,
            window =>
            {
                windows.Add(window);
                return 1;
            },
            resultCount: count => count);

        Assert.Equal([30, 60, 120], windows);
        Assert.Equal(RecommendationOverFetch.MaximumRounds, outcome.RoundsUsed);
        Assert.Equal(1, outcome.Result);
    }

    /// <summary>
    /// Janela que não cresce não traz candidata nova: a varredura devolveu menos do que a rodada 1 pediria, então
    /// repetir o pós-processamento cobraria CPU para chegar ao MESMO resultado. Para antes do teto, de propósito.
    /// </summary>
    [Fact]
    public void Varredura_menor_que_a_janela_nao_gasta_rodada_repetindo_o_mesmo_conjunto()
    {
        var windows = new List<int>();

        RecommendationOverFetchOutcome<int> outcome = RecommendationOverFetch.Resolve(
            limit: 10,
            availableCandidates: 4,
            window =>
            {
                windows.Add(window);
                return 2;
            },
            resultCount: count => count);

        Assert.Equal([4], windows);
        Assert.Equal(1, outcome.RoundsUsed);
        Assert.Equal(4, outcome.CandidatesConsidered);
    }

    /// <summary>
    /// O prefixo da janela (E4.12): é o que faz a rodada cortar o funil INTEIRO. Aritmética pura sobre uma lista já
    /// ordenada — a janela maior que a lista devolve a própria lista, sem cópia, porque a rodada que não corta nada
    /// não deve pagar alocação.
    /// </summary>
    [Fact]
    public void Prefixo_da_janela_corta_a_lista_ordenada()
    {
        int[] candidates = [9, 8, 7, 6, 5];
        int[] firstThree = [9, 8, 7];
        int[] empty = [];

        Assert.Equal(firstThree, RecommendationOverFetch.Prefix(candidates, window: 3));
        Assert.Same(candidates, RecommendationOverFetch.Prefix(candidates, window: 5));
        Assert.Same(candidates, RecommendationOverFetch.Prefix(candidates, window: 50));
        Assert.Empty(RecommendationOverFetch.Prefix(candidates, window: 0));
        Assert.Empty(RecommendationOverFetch.Prefix(empty, window: 30));
    }

    [Fact]
    public void Limite_nao_positivo_e_rejeitado()
    {
        Assert.Throws<DomainException>(
            () => RecommendationOverFetch.Resolve(
                limit: 0, availableCandidates: 30, window => 0, resultCount: count => count));
    }
}
