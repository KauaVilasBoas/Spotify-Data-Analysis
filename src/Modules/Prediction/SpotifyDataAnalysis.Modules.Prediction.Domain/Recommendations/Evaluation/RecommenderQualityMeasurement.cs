namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

/// <summary>
/// O resultado dos proxies que dependem da configuração de gênero (1 e 2), medidos sobre UMA amostra e UMA
/// <see cref="RecommenderEvaluationSetting"/>. É uma linha da tabela de calibração.
/// </summary>
/// <param name="Setting">A configuração de ranking sob a qual esta linha foi medida.</param>
/// <param name="TopN">Tamanho do top-N usado na medição.</param>
/// <param name="GenreCoherence">Proxy 1 — o quanto o top-N compartilha o gênero da semente.</param>
/// <param name="SelfExclusion">Proxy 2 — a semente jamais aparece no próprio top-N.</param>
public sealed record RecommenderQualityMeasurement(
    RecommenderEvaluationSetting Setting,
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
/// <param name="SeedsWithoutUsableGenre">Sementes descartadas por não terem gênero — não há coerência a medir.</param>
/// <param name="SeedsMissingFromIndex">Sementes que não estavam no índice; contadas em vez de silenciadas.</param>
/// <param name="ImputedSeeds">Sementes com features imputadas (DP-F) — hoje zero no catálogo, medido e não presumido.</param>
/// <param name="MeanCoherence">Média por semente da fração do top-N com o mesmo gênero. Em [0, 1].</param>
/// <param name="SaturatedSeeds">Sementes cujo top-N inteiro é do mesmo gênero — o sintoma de saturação do card.</param>
/// <param name="MeanImputedNeighbors">Média de vizinhos imputados por top-N; o sinal da DP-F no resultado.</param>
public sealed record GenreCoherenceProxy(
    int SeedsEvaluated,
    int SeedsWithoutUsableGenre,
    int SeedsMissingFromIndex,
    int ImputedSeeds,
    double MeanCoherence,
    int SaturatedSeeds,
    double MeanImputedNeighbors)
{
    /// <summary>A fração de sementes cujo top-N é 100% do mesmo gênero — a leitura direta da saturação.</summary>
    public double SaturationRate => SeedsEvaluated == 0 ? 0.0 : (double)SaturatedSeeds / SeedsEvaluated;
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
public sealed record DuplicateProximityProxy(
    int GroupsEvaluated,
    int SeedsEvaluated,
    double MeanRecall,
    double HitRate,
    double MeanFirstSiblingRank,
    double MeanSiblingCosine,
    int SeedsWithFullyDuplicatedTopK);
