namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Os parâmetros e o cálculo do over-fetch do recomendador (E4.11): quando há pós-processamento (dedup e/ou blend),
/// o handler pede mais candidatas do que o <c>limit</c> pedido, para que o colapso ou a fusão não reduzam o resultado
/// abaixo do contratado. Esta classe é a única fonte da verdade — handler de produção, harness de avaliação e
/// testes de qualidade lêem o mesmo valor e a mesma fórmula.
///
/// <para><b>Por que 3× e piso 10 (calibrado no E4.4):</b> o maior grupo de duplicatas observado tinha 54 faixas, mas
/// grupos desse tamanho são raríssimos no topo de uma semente típica. 3× cobre com larga folga esse cenário sem
/// varrer o catálogo além do necessário — a varredura kNN é O(n) no tamanho do índice, não no tamanho do
/// over-fetch, então o custo de ir de 10 a 30 é desprezível; o custo de ir de 1 a 10 (o piso) é o que evita a
/// lista vazia quando <c>limit=1</c>.</para>
/// </summary>
public static class RecommendationOverFetch
{
    /// <summary>
    /// Fator multiplicador do over-fetch. 3× cobre o cenário medido no E4.4 (maior grupo de duplicatas = 54,
    /// raríssimo no topo de uma semente típica) sem varrer o catálogo além do necessário.
    /// </summary>
    public const int Factor = 3;

    /// <summary>
    /// Piso do over-fetch: limites pequenos ainda precisam de margem de colapso (ex.: <c>limit=1</c> pede 10).
    /// </summary>
    public const int Minimum = 10;

    /// <summary>
    /// Calcula quantas candidatas buscar quando há pós-processamento. A fórmula <c>max(limit × Factor, Minimum)</c>
    /// garante margem suficiente tanto para limites grandes quanto para limites menores que o piso.
    /// </summary>
    /// <param name="limit">O top-N pedido pelo chamador.</param>
    public static int CountFor(int limit) => Math.Max(limit * Factor, Minimum);
}
