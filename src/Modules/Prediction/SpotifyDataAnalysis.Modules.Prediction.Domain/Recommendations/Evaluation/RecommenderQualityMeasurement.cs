namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

/// <summary>
/// O resultado dos proxies que dependem da configuração de gênero (1 e 2), medidos sobre UMA amostra e UMA
/// <see cref="RecommenderEvaluationSetting"/>. É uma linha da tabela de calibração.
/// </summary>
/// <param name="Setting">A configuração de ranking sob a qual esta linha foi medida.</param>
/// <param name="SeedPopulation">
/// A população de sementes que produziu esta linha (E4.12) — o terceiro eixo da identidade, ao lado da configuração e
/// do top-N. Sem ele, o ganho da coerência podia ser calculado entre duas populações diferentes e o gate aprovava a
/// razão de dois números que ninguém comparou.
/// </param>
/// <param name="TopN">Tamanho do top-N usado na medição.</param>
/// <param name="GenreCoherence">Proxy 1 — o quanto o top-N compartilha o gênero da semente.</param>
/// <param name="SelfExclusion">Proxy 2 — a semente jamais aparece no próprio top-N.</param>
public sealed record RecommenderQualityMeasurement(
    RecommenderEvaluationSetting Setting,
    RecommenderEvaluationPopulation SeedPopulation,
    int TopN,
    GenreCoherenceProxy GenreCoherence,
    SelfExclusionProxy SelfExclusion);

/// <summary>
/// Proxy 1 — coerência de gênero: a fração média do top-N que compartilha o gênero da semente.
///
/// <para><b>Como ler:</b> o gênero comparado aqui é sempre o rótulo do índice, dos dois lados, INDEPENDENTE de o
/// gênero ter pesado ou não no ranking. Por isso o mesmo número é comparável entre <c>off</c> e cada peso — é essa
/// comparação pareada que responde se o boost melhora a coerência ou se apenas a satura, virando filtro
/// disfarçado.</para>
/// </summary>
/// <param name="SeedsEvaluated">Sementes que entraram na média (estão no índice e têm gênero utilizável).</param>
/// <param name="SeedsWithoutUsableGenre">
/// Sementes descartadas porque o RÓTULO de gênero não serve: ausente, em branco, ou de faixa imputada (DP-F). Não há
/// coerência a medir sem um gênero de referência.
///
/// <para><b>Não inclui</b> a semente que tinha gênero e recebeu top-N vazio — essa é a
/// <paramref name="SeedsWithEmptyTopN"/>. Contar as duas juntas publicava um defeito do FUNIL como falta de rótulo, e
/// quem lê a tabela decidiria enriquecer metadata quando o problema estava no ranking.</para>
/// </param>
/// <param name="SeedsWithEmptyTopN">
/// Sementes com gênero utilizável cujo top-N veio VAZIO — não há vizinho com que comparar gênero. A causa é o FUNIL
/// (filtro duro de gênero sem candidata elegível, ou índice sem outra faixa), não o rótulo da semente.
/// </param>
/// <param name="SeedsMissingFromIndex">Sementes que não estavam no índice; contadas em vez de silenciadas.</param>
/// <param name="ImputedSeeds">Sementes com features imputadas (DP-F) — hoje zero no catálogo, medido e não presumido.</param>
/// <param name="MeanCoherence">Média por semente da fração do top-N com o mesmo gênero. Em [0, 1].</param>
/// <param name="SaturatedSeeds">Sementes cujo top-N inteiro é do mesmo gênero — o sintoma de saturação do card.</param>
/// <param name="MeanImputedNeighbors">Média de vizinhos imputados por top-N; o sinal da DP-F no resultado.</param>
/// <param name="CoherenceStandardDeviation">
/// Desvio-padrão da coerência ENTRE as sementes avaliadas.
///
/// <para><b>Não é enfeite:</b> comparar duas configurações por médias nuas não distingue efeito de sorteio da
/// amostra. Com o desvio e o <paramref name="SeedsEvaluated"/> na mão, quem lê calcula o erro padrão e decide se um
/// delta de 0,05 é sinal ou ruído — que é exatamente a pergunta que a tabela de calibração do boost e a comparação
/// content × blend levantam.</para>
/// </param>
public sealed record GenreCoherenceProxy(
    int SeedsEvaluated,
    int SeedsWithoutUsableGenre,
    int SeedsWithEmptyTopN,
    int SeedsMissingFromIndex,
    int ImputedSeeds,
    double MeanCoherence,
    int SaturatedSeeds,
    double MeanImputedNeighbors,
    double CoherenceStandardDeviation = 0.0)
{
    /// <summary>A fração de sementes cujo top-N é 100% do mesmo gênero — a leitura direta da saturação.</summary>
    public double SaturationRate => SeedsEvaluated == 0 ? 0.0 : (double)SaturatedSeeds / SeedsEvaluated;

    /// <summary>
    /// Erro padrão da média (<c>σ/√n</c>) — a régua com que dois valores de coerência devem ser comparados.
    /// </summary>
    public double CoherenceStandardError =>
        SeedsEvaluated == 0 ? 0.0 : CoherenceStandardDeviation / Math.Sqrt(SeedsEvaluated);
}

/// <summary>
/// Proxy 2 — autoexclusão: a garantia de que a semente nunca aparece no próprio top-N, verificada em ESCALA sobre
/// a amostra inteira, e não num caso unitário. É binário por natureza: qualquer violação é um defeito, não uma
/// degradação de qualidade.
/// </summary>
/// <param name="SeedsEvaluated">Sementes varridas.</param>
/// <param name="Violations">Quantas vezes a semente apareceu no próprio top-N. O valor aceitável é zero.</param>
public sealed record SelfExclusionProxy(int SeedsEvaluated, int Violations)
{
    /// <summary>Se a garantia se manteve em toda a amostra.</summary>
    public bool IsClean => Violations == 0;
}

/// <summary>
/// Proxy 3 — proximidade de duplicatas: faixas que o catálogo sabe serem a mesma obra devem se reencontrar no
/// topo. É o único proxy independente de gênero e, por isso, o único que testa o espaço de similaridade sem a
/// circularidade de medir gênero num ranking que boosta gênero. Medido sempre com o gênero DESLIGADO.
///
/// <para><b>Recall normalizado pelo teto alcançável (DP-4):</b> um grupo com 54 membros não cabe num top-10, logo
/// dividir os acertos por <c>n-1</c> mediria o K, não o recomendador. O denominador é <c>min(n-1, K)</c> — um
/// recall de 1,0 significa "reencontrou todos os irmãos que caberiam".</para>
/// </summary>
/// <param name="GroupsEvaluated">Grupos de duplicatas efetivamente usados.</param>
/// <param name="SeedsEvaluated">Sementes varridas (cada membro de cada grupo que está no índice).</param>
/// <param name="MeanRecall">Média por semente de <c>acertos / min(n-1, K)</c>. Em [0, 1].</param>
/// <param name="HitRate">Fração de sementes com ao menos um irmão no top-K.</param>
/// <param name="MeanFirstSiblingRank">Posição média (1-based) do primeiro irmão, entre as sementes que acertaram.</param>
/// <param name="MeanSiblingCosine">Cosseno médio dos irmãos reencontrados — quão idênticas as duplicatas de fato são.</param>
/// <param name="SeedsWithFullyDuplicatedTopK">Sementes cujo top-K inteiro é irmão. Dimensiona a dor que o E4.7 resolve.</param>
/// <param name="SeedsWithRedundantSiblings">
/// Sementes cujo top-K traz DUAS ou mais versões da mesma obra — a repetição que o dedup do E4.7 promete eliminar.
/// Com <c>dedupe=false</c> mede a dor; com <c>dedupe=true</c> o alvo é zero (DP-2 do E4.8).
/// </param>
/// <param name="SeedsWithIncompleteTopK">
/// Sementes que pediram <c>topK</c> recomendações e receberam MENOS.
///
/// <para><b>Existe porque "top-K 100% duplicado" é ambíguo sozinho:</b> o indicador pode significar "a lista repete
/// a mesma obra" (a dor que o dedup resolve) ou "a lista encolheu" (um efeito colateral do dedup). São leituras
/// opostas, e distingui-las por raciocínio seria adivinhar — este contador as separa por medição.</para>
/// </param>
/// <param name="Setting">
/// A configuração que produziu estes números. É campo obrigatório, e não documentação: o gate do E4.8 RECUSA
/// medições cujo <c>dedupe</c> não corresponda ao limiar que está prestes a aplicar — foi medir sem saber a
/// configuração que tornou os números do E4.4 obsoletos sem ninguém perceber.
/// </param>
public sealed record DuplicateProximityProxy(
    int GroupsEvaluated,
    int SeedsEvaluated,
    double MeanRecall,
    double HitRate,
    double MeanFirstSiblingRank,
    double MeanSiblingCosine,
    int SeedsWithFullyDuplicatedTopK,
    int SeedsWithRedundantSiblings,
    int SeedsWithIncompleteTopK,
    RecommenderEvaluationSetting Setting);

/// <summary>
/// O top-N que o endpoint devolveria para UMA semente, com o custo que produzi-lo exigiu (E4.9).
///
/// <para><b>As duas contagens de custo não são enfeite:</b> o over-fetch adaptativo paga por rodada de
/// pós-processamento, e sem elas a distribuição de tamanho melhoraria sem ninguém saber a que preço. São a única
/// forma de distinguir "quase toda semente resolve na primeira rodada" de "o teto é atingido sempre".</para>
/// </summary>
/// <param name="Neighbors">O top-N final, já pós-processado (blend e/ou dedup, conforme a configuração).</param>
/// <param name="RoundsUsed">Quantas rodadas de pós-processamento foram necessárias (1 quando a primeira bastou).</param>
/// <param name="CandidatesConsidered">Quantas candidatas da varredura entraram na última rodada.</param>
public sealed record EndpointTopN(
    IReadOnlyList<TrackSimilarity> Neighbors, int RoundsUsed, int CandidatesConsidered);

/// <summary>
/// Proxy 4 — a DISTRIBUIÇÃO do tamanho do resultado (E4.9): de quantas sementes o endpoint entrega menos do que o
/// top-N pedido, e quantas rodadas de over-fetch isso custou.
///
/// <para><b>Por que é um proxy próprio, e não um campo do proxy 3:</b> o proxy 3 mede o gênero SEMPRE desligado (é o
/// único guarda não circular), e o defeito que este proxy persegue foi medido no default do endpoint —
/// <c>boost | dedupe=on | content</c>. Medir a distribuição na configuração forçada do proxy 3 responderia sobre um
/// ranking que o usuário não recebe, exatamente o erro que o E4.10 corrigiu neste módulo.</para>
///
/// <para><b>O critério é o TAMANHO do resultado, não o fator de over-fetch</b> (E4.9): o fator é implementação, a
/// lista curta é o que o usuário vê.</para>
/// </summary>
/// <param name="SeedsEvaluated">Sementes que estavam no índice e produziram um top-N.</param>
/// <param name="SeedsMissingFromIndex">Sementes fora do índice; contadas em vez de silenciadas.</param>
/// <param name="SeedsBelowTopN">Sementes que pediram <paramref name="TopN"/> e receberam MENOS.</param>
/// <param name="SeedsWithSingleResult">
/// Sementes que receberam EXATAMENTE uma recomendação. Contado à parte do <paramref name="SeedsBelowTopN"/> porque é
/// outro defeito: uma lista de nove itens é curta, uma lista de um item lê como recomendador quebrado.
/// </param>
/// <param name="SeedsWithEmptyResult">Sementes que receberam ZERO recomendações — o caso extremo, medido e não presumido.</param>
/// <param name="MeanResultSize">Tamanho médio do resultado entre as sementes avaliadas.</param>
/// <param name="SeedsByRound">
/// Quantas sementes resolveram em cada rodada, da primeira à última — o custo do adaptativo, por semente. A posição
/// 0 é a primeira rodada (o over-fetch de sempre); as seguintes só existem quando o funil derrubou candidatas.
/// </param>
/// <param name="Setting">A configuração que produziu estes números — obrigatória, pela mesma razão do proxy 3.</param>
/// <param name="SeedPopulation">
/// A população de sementes medida — obrigatória, e aqui é o campo MAIS importante (E4.12). Os dois limiares deste
/// proxy foram calibrados sobre os membros dos grupos grandes de quase-duplicatas, onde a lista curta valia 0,3583
/// antes do over-fetch adaptativo. Numa amostra sorteada o fenômeno é ~0, então a mesma régua passaria por vacuidade —
/// e reverter o over-fetch adaptativo deixaria o gate verde. Por isso o gate cobra a população, e não só a
/// configuração.
/// </param>
/// <param name="TopN">O top-N pedido, contra o qual "abaixo do pedido" é definido.</param>
public sealed record RecommendationSizeProxy(
    int SeedsEvaluated,
    int SeedsMissingFromIndex,
    int SeedsBelowTopN,
    int SeedsWithSingleResult,
    int SeedsWithEmptyResult,
    double MeanResultSize,
    IReadOnlyList<int> SeedsByRound,
    RecommenderEvaluationSetting Setting,
    RecommenderEvaluationPopulation SeedPopulation,
    int TopN)
{
    /// <summary>A fração de sementes que recebeu menos do que pediu — a leitura direta do defeito do E4.9.</summary>
    public double ShortResultRate => SeedsEvaluated == 0 ? 0.0 : (double)SeedsBelowTopN / SeedsEvaluated;

    /// <summary>Quantas rodadas a semente mais custosa precisou; 0 quando nenhuma semente foi avaliada.</summary>
    public int MaximumRoundsUsed
    {
        get
        {
            for (int round = SeedsByRound.Count; round > 0; round--)
            {
                if (SeedsByRound[round - 1] > 0)
                    return round;
            }

            return 0;
        }
    }
}
