using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Os parâmetros e o cálculo do over-fetch do recomendador (E4.11): quando há pós-processamento (dedup e/ou blend),
/// o handler pede mais candidatas do que o <c>limit</c> pedido, para que o colapso ou a fusão não reduzam o resultado
/// abaixo do contratado. Esta classe é a única fonte da verdade — handler de produção, avaliador de qualidade e
/// harness de avaliação leem o mesmo valor, a mesma fórmula e o MESMO laço adaptativo.
///
/// <para><b>Por que 3× e piso 10 (calibrado no E4.4):</b> o maior grupo de duplicatas observado tinha 54 faixas, mas
/// grupos desse tamanho são raríssimos no topo de uma semente típica. 3× cobre com larga folga esse cenário sem
/// varrer o catálogo além do necessário — a varredura kNN é O(n) no tamanho do índice, não no tamanho do
/// over-fetch, então o custo de ir de 10 a 30 é desprezível; o custo de ir de 1 a 10 (o piso) é o que evita a
/// lista vazia quando <c>limit=1</c>.</para>
///
/// <para><b>Por que 3× fixo não bastou (E4.9):</b> medido sobre 1.895 sementes de grupos de 11+ quase-duplicatas,
/// 679 recebiam menos de 10 recomendações e 227 recebiam exatamente 1 — para essas sementes o funil (dedup do E4.7 +
/// filtros do E4.3) derruba mais candidatas do que o fator fixo previu. O over-fetch passou a ser ADAPTATIVO: a
/// janela dobra por rodada, até <see cref="MaximumRounds"/>, e só dobra quando o resultado pós-filtro ficou abaixo
/// do pedido.</para>
///
/// <para><b>Rodada extra NÃO é varredura extra.</b> A varredura kNN é O(n) no tamanho do índice (89.740 faixas), não
/// no over-fetch: refazê-la por rodada triplicaria o custo por requisição num host de free tier. Em vez disso a
/// varredura é feita UMA vez na janela máxima e cada rodada reaproveita um PREFIXO dela. Isso é exato, não
/// aproximado: o <see cref="TopNeighborHeap"/> ordena por similaridade com desempate total por <c>trackId</c>, então
/// o prefixo de tamanho <c>w</c> do top-máximo é bit a bit o top-<c>w</c> que uma segunda varredura devolveria. O que
/// as rodadas repetem é só o pós-processamento (blend e dedup), que é O(janela²) no pior caso — dezenas de itens,
/// não o catálogo.</para>
/// </summary>
public static class RecommendationOverFetch
{
    /// <summary>
    /// Fator multiplicador do over-fetch da PRIMEIRA rodada. 3× cobre o cenário medido no E4.4 (maior grupo de
    /// duplicatas = 54, raríssimo no topo de uma semente típica) sem varrer o catálogo além do necessário.
    /// </summary>
    public const int Factor = 3;

    /// <summary>
    /// Piso do over-fetch: limites pequenos ainda precisam de margem de colapso (ex.: <c>limit=1</c> pede 10).
    /// </summary>
    public const int Minimum = 10;

    /// <summary>
    /// Teto de rodadas (E4.9, DP-1): três. A janela dobra a cada rodada, então três rodadas cobrem 4× a janela
    /// inicial — para o <c>limit=10</c> do endpoint, de 30 a 120 candidatas. O teto é critério de aceite, não detalhe
    /// de implementação: uma semente patológica (grupo gigante de quase-duplicatas, gênero sem vizinho elegível) tem
    /// de parar de forma determinística em vez de varrer o catálogo atrás de um K que não existe.
    /// </summary>
    public const int MaximumRounds = 3;

    /// <summary>
    /// Teto ABSOLUTO de candidatas consideradas, qualquer que seja o <c>limit</c> (E4.9, DP-1). Com o teto de
    /// <c>limit</c> em 50, a terceira rodada pede 600 — 0,7% do catálogo de 89.740 faixas. O teto existe para que o
    /// custo por requisição seja limitado por uma constante conhecida, e não pelo produto de dois parâmetros.
    /// </summary>
    public const int MaximumCandidates = 600;

    /// <summary>
    /// Calcula quantas candidatas a PRIMEIRA rodada busca. A fórmula <c>max(limit × Factor, Minimum)</c> garante
    /// margem suficiente tanto para limites grandes quanto para limites menores que o piso.
    /// </summary>
    /// <param name="limit">O top-N pedido pelo chamador.</param>
    public static int CountFor(int limit) => Math.Max(limit * Factor, Minimum);

    /// <summary>
    /// A janela de candidatas da rodada informada: a primeira é <see cref="CountFor"/> e cada rodada seguinte dobra,
    /// limitada por <see cref="MaximumCandidates"/>.
    /// </summary>
    /// <param name="limit">O top-N pedido pelo chamador.</param>
    /// <param name="round">A rodada, de 1 a <see cref="MaximumRounds"/>.</param>
    /// <exception cref="DomainException">Quando a rodada está fora de [1, <see cref="MaximumRounds"/>].</exception>
    public static int CountForRound(int limit, int round)
    {
        if (round is < 1 || round > MaximumRounds)
            throw new DomainException(
                $"A rodada de over-fetch deve estar em [1, {MaximumRounds}]. Recebida: {round}.");

        long window = (long)CountFor(limit) << (round - 1);

        return (int)Math.Min(window, MaximumCandidates);
    }

    /// <summary>
    /// Quantas candidatas a varredura precisa retornar para servir a TODAS as rodadas — a janela da última rodada.
    /// É o número que um chamador passa ao índice: uma varredura só, e as rodadas leem prefixos dela.
    /// </summary>
    /// <param name="limit">O top-N pedido pelo chamador.</param>
    public static int MaximumCountFor(int limit) => CountForRound(limit, MaximumRounds);

    /// <summary>
    /// Roda o over-fetch adaptativo: pós-processa o prefixo da rodada 1 e, SÓ se o resultado ficou abaixo de
    /// <paramref name="limit"/>, repete numa janela dobrada — até <see cref="MaximumRounds"/> ou até a janela deixar
    /// de crescer (a varredura acabou). Devolve o último resultado com o custo que ele exigiu.
    ///
    /// <para><b>Nunca especulativo:</b> a rodada seguinte só existe porque a anterior devolveu menos que o pedido. Se
    /// a primeira já entrega <paramref name="limit"/> itens, o pós-processamento roda exatamente uma vez — o
    /// comportamento é bit a bit o de antes do E4.9 para as sementes que já estavam saudáveis.</para>
    ///
    /// <para><b>É aritmética pura de domínio:</b> não conhece índice, catálogo nem candidato. O chamador fecha sobre
    /// a sua própria varredura e devolve o resultado já pós-processado do prefixo pedido — é isso que permite ao
    /// handler (que trabalha com explicabilidade rica) e ao avaliador (que trabalha com o ranking nu) compartilharem
    /// o MESMO laço em vez de duas cópias que divergem.</para>
    /// </summary>
    /// <typeparam name="TResult">O que o pós-processamento produz (lista de itens, ou item + diagnóstico).</typeparam>
    /// <param name="limit">O top-N pedido; o laço para quando o resultado o alcança.</param>
    /// <param name="availableCandidates">Quantas candidatas a varredura devolveu — o teto real da janela.</param>
    /// <param name="postProcessPrefix">
    /// Pós-processa as N primeiras candidatas da varredura, com N = a janela da rodada. É chamado uma vez por rodada.
    /// </param>
    /// <param name="resultCount">Quantos itens o resultado tem — o critério de parada.</param>
    /// <exception cref="DomainException">Quando <paramref name="limit"/> não é positivo.</exception>
    public static RecommendationOverFetchOutcome<TResult> Resolve<TResult>(
        int limit,
        int availableCandidates,
        Func<int, TResult> postProcessPrefix,
        Func<TResult, int> resultCount)
    {
        ArgumentNullException.ThrowIfNull(postProcessPrefix);
        ArgumentNullException.ThrowIfNull(resultCount);

        if (limit <= 0)
            throw new DomainException($"O limite do over-fetch adaptativo deve ser positivo. Recebido: {limit}.");

        int window = Math.Min(CountForRound(limit, 1), availableCandidates);
        TResult result = postProcessPrefix(window);
        int roundsUsed = 1;

        while (roundsUsed < MaximumRounds && resultCount(result) < limit)
        {
            int widened = Math.Min(CountForRound(limit, roundsUsed + 1), availableCandidates);

            // Janela que não cresce não traz candidata nova: repetir o pós-processamento devolveria o MESMO resultado
            // e cobraria CPU por nada. É o que faz uma semente patológica parar antes do teto em vez de nele.
            if (widened <= window)
                break;

            result = postProcessPrefix(widened);
            window = widened;
            roundsUsed++;
        }

        return new RecommendationOverFetchOutcome<TResult>(result, roundsUsed, window);
    }
}

/// <summary>
/// O resultado de uma resolução adaptativa de over-fetch: o que o pós-processamento produziu e o que isso custou.
///
/// <para>O custo viaja junto de propósito. Sem ele, "a distribuição de tamanho melhorou" seria uma afirmação sem
/// preço, e o risco registrado no E4.9 é justamente trocar uma dívida de qualidade por uma de latência.</para>
/// </summary>
/// <typeparam name="TResult">O que o pós-processamento produz.</typeparam>
/// <param name="Result">O resultado da última rodada executada.</param>
/// <param name="RoundsUsed">Quantas rodadas rodaram; 1 quando a primeira já entregou o pedido.</param>
/// <param name="CandidatesConsidered">O tamanho da janela da última rodada.</param>
public readonly record struct RecommendationOverFetchOutcome<TResult>(
    TResult Result, int RoundsUsed, int CandidatesConsidered);
