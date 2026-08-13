using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// A regra de como o gênero da semente reforça (boost) ou corta (filtro) o ranking do cosine — o estágio de gênero
/// do híbrido leve do E4.3 (DP-C/DP-1). É uma Strategy/Policy imutável, construída UMA vez por consulta a partir da
/// semente e do modo pedido, e consultada pelo <see cref="SimilarityIndex"/> a cada candidata durante a varredura.
///
/// <para><b>Boost (default):</b> <c>scoreHíbrido = cosine + peso·[mesmo gênero da semente]</c>. O bônus é aditivo e
/// só incide quando a candidata compartilha o gênero UTILIZÁVEL da semente. Como o boost concorre DENTRO da
/// varredura (não num re-rank do top-N já cortado), uma candidata do mesmo gênero que ficaria de fora por pouco no
/// cosine puro pode entrar — que é o efeito pretendido pela DP-C.</para>
///
/// <para><b>Fallback gracioso (obrigatório do card):</b> se a semente NÃO tem gênero utilizável — ausente, em
/// branco, ou imputado (DP-F: não se boosta com base em rótulo estimado) — a política degenera para o cosine puro
/// e sinaliza o motivo em <see cref="FellBackToCosineOnly"/>, em vez de filtrar para vazio em silêncio. O mesmo vale
/// para o filtro duro: sem gênero de referência não há como cortar, então não se corta.</para>
///
/// <para><b>Imputado nunca boosta (DP-F):</b> uma candidata com features/gênero imputados não recebe bônus mesmo
/// coincidindo o rótulo — o boost premia coincidência MEDIDA, não estimada; e a semente imputada não boosta
/// ninguém. Assim o gênero imputado nunca passa por medido no ranking.</para>
/// </summary>
public sealed class GenreAffinityPolicy
{
    /// <summary>
    /// Peso default do boost de gênero. Calibrado para REORDENAR sem virar filtro disfarçado: o cosine no espaço
    /// z-score deste recomendador fica tipicamente alto (0,9+) entre vizinhos próximos, e as diferenças de cosine
    /// no topo são de centésimos, então basta um bônus pequeno para desempatar a favor do mesmo gênero e resgatar
    /// bons vizinhos que perderam por pouco.
    ///
    /// <para><b>Valor CALIBRADO pelo E4.4</b> (era 0,15 provisório) sobre o catálogo real — 300 sementes fixas,
    /// top-10, proxy de coerência de gênero medido do cosine puro até 0,15:</para>
    /// <code>
    /// peso   coerência   saturação (top-10 de um só gênero)
    /// off      0,1347      0,0100
    /// 0,02     0,3283      0,0900
    /// 0,05     0,5833      0,3100   ← eleito
    /// 0,08     0,7563      0,5167
    /// 0,10     0,8270      0,6567
    /// 0,15     0,9143      0,8233
    /// </code>
    /// <para>A curva não satura em lugar nenhum da faixa, então ela é informativa — e mostra que 0,15 levava 82% das
    /// sementes a um top-10 de gênero único, ou seja, para quatro em cada cinco consultas o "boost" já era um filtro
    /// duro, tornando o modo <see cref="GenreRankingMode.Boost"/> indistinguível de
    /// <see cref="GenreRankingMode.SameGenreOnly"/> e apagando o eixo de relaxamento da DP-C.</para>
    /// <para>0,05 é o MAIOR peso cuja saturação permanece minoritária (0,31): mais que quadruplica a coerência sobre
    /// o cosine puro (0,1347 → 0,5833) e ainda deixa 69% das sementes com pelo menos um vizinho de outro gênero no
    /// top-10 — desempate, que é o que a DP-C pediu, e não filtro. O gate do E4.4 defende os dois lados dessa
    /// escolha (ganho mínimo e teto de saturação), então subir este número silenciosamente reprova a suíte.</para>
    /// </summary>
    public const double DefaultBoostWeight = 0.05;

    private readonly string? _seedGenre;

    private GenreAffinityPolicy(
        GenreRankingMode mode, double boostWeight, string? seedGenre, bool fellBackToCosineOnly)
    {
        Mode = mode;
        BoostWeight = boostWeight;
        _seedGenre = seedGenre;
        FellBackToCosineOnly = fellBackToCosineOnly;
    }

    /// <summary>O modo EFETIVO aplicado — pode ser <see cref="GenreRankingMode.Off"/> por fallback, mesmo que o pedido fosse boost/filtro.</summary>
    public GenreRankingMode Mode { get; }

    /// <summary>Peso do bônus aditivo do boost (só relevante em <see cref="GenreRankingMode.Boost"/>).</summary>
    public double BoostWeight { get; }

    /// <summary>
    /// True quando o modo pedido era boost/filtro mas a semente não tinha gênero utilizável — o ranking caiu no
    /// cosine puro por segurança. É o sinal que o consumidor propaga ao response (nunca filtrar/degradar em silêncio).
    /// </summary>
    public bool FellBackToCosineOnly { get; }

    /// <summary>O gênero da semente efetivamente em uso no ranking, ou null quando o gênero não pesa (off/fallback).</summary>
    public string? SeedGenre => Mode == GenreRankingMode.Off ? null : _seedGenre;

    /// <summary>
    /// Constrói a política para uma consulta. <paramref name="seedGenre"/> e <paramref name="seedGenreIsImputed"/>
    /// descrevem o gênero da semente; a política decide sozinha se ele é utilizável e, se não for, faz o fallback
    /// gracioso para <see cref="GenreRankingMode.Off"/> registrando o motivo.
    /// </summary>
    /// <param name="mode">Modo pedido pelo cliente (o eixo de relaxamento da DP-C).</param>
    /// <param name="seedGenre">Gênero da semente (já normalizado pelo Catalog em lowercase), ou null/ausente.</param>
    /// <param name="seedGenreIsImputed">Se a semente teve features imputadas — logo o gênero é estimado (DP-F).</param>
    /// <param name="boostWeight">Peso do boost; ignorado fora do modo boost. Deve ser não-negativo.</param>
    /// <exception cref="DomainException">Quando o peso do boost é negativo (bônus negativo seria uma penalidade escondida).</exception>
    public static GenreAffinityPolicy Create(
        GenreRankingMode mode,
        string? seedGenre,
        bool seedGenreIsImputed,
        double boostWeight = DefaultBoostWeight)
    {
        if (boostWeight < 0)
            throw new DomainException(
                $"O peso do boost de gênero não pode ser negativo (recebido: {boostWeight}): um bônus negativo " +
                "viraria uma penalidade escondida ao mesmo gênero, o oposto do que o híbrido pretende.");

        if (mode == GenreRankingMode.Off)
            return new GenreAffinityPolicy(GenreRankingMode.Off, boostWeight, seedGenre: null, fellBackToCosineOnly: false);

        // Semente sem gênero utilizável (ausente/branco ou imputado): o híbrido não tem sobre o que boostar/filtrar.
        // Cai no cosine puro e SINALIZA — nunca filtra para vazio nem boosta com base num rótulo estimado (DP-F).
        bool seedGenreIsUsable = !string.IsNullOrWhiteSpace(seedGenre) && !seedGenreIsImputed;
        if (!seedGenreIsUsable)
            return new GenreAffinityPolicy(GenreRankingMode.Off, boostWeight, seedGenre: null, fellBackToCosineOnly: true);

        return new GenreAffinityPolicy(mode, boostWeight, seedGenre, fellBackToCosineOnly: false);
    }

    /// <summary>A política neutra: o gênero não pesa (cosine puro do E4.1). Usada quando o modo é off por escolha do cliente.</summary>
    public static GenreAffinityPolicy CosineOnly() =>
        new(GenreRankingMode.Off, DefaultBoostWeight, seedGenre: null, fellBackToCosineOnly: false);

    /// <summary>
    /// Se uma candidata é ELEGÍVEL a entrar no ranking sob esta política. Só o filtro duro exclui: em
    /// <see cref="GenreRankingMode.SameGenreOnly"/>, sobrevive apenas quem compartilha o gênero utilizável da
    /// semente. Nos demais modos toda candidata é elegível (o boost não exclui ninguém, só reordena).
    /// </summary>
    public bool IsCandidateEligible(string? candidateGenre, bool candidateGenreIsImputed) =>
        Mode != GenreRankingMode.SameGenreOnly || SharesSeedGenre(candidateGenre, candidateGenreIsImputed);

    /// <summary>
    /// O bônus de gênero somado ao cosine da candidata. É <c>peso</c> quando ela compartilha o gênero utilizável da
    /// semente em modo boost; zero caso contrário (inclusive no filtro duro, onde a coerência já é garantida pela
    /// elegibilidade e um bônus só distorceria a ordem por cosine dentro do mesmo gênero).
    /// </summary>
    public double GenreBonusFor(string? candidateGenre, bool candidateGenreIsImputed) =>
        Mode == GenreRankingMode.Boost && SharesSeedGenre(candidateGenre, candidateGenreIsImputed)
            ? BoostWeight
            : 0.0;

    /// <summary>
    /// Uma candidata compartilha o gênero da semente quando ambos os rótulos são utilizáveis (não vazios, não
    /// imputados) e IGUAIS. A comparação é ordinal — os gêneros já vêm normalizados em lowercase pelo Catalog, então
    /// igualdade ordinal é igualdade de rótulo, sem o custo de uma comparação culture-aware por candidata.
    /// </summary>
    private bool SharesSeedGenre(string? candidateGenre, bool candidateGenreIsImputed)
    {
        if (Mode == GenreRankingMode.Off || string.IsNullOrWhiteSpace(_seedGenre))
            return false;

        if (candidateGenreIsImputed || string.IsNullOrWhiteSpace(candidateGenre))
            return false;

        return string.Equals(_seedGenre, candidateGenre, StringComparison.Ordinal);
    }
}
