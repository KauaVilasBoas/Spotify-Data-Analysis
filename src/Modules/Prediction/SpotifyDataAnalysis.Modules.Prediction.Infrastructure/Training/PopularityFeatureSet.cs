namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

/// <summary>
/// Os blocos de features que o E3.3 mede incrementalmente. É um enum de FLAGS porque a eleição do campeão é
/// combinatória: o card exige medir a base sozinha, cada bloco isolado sobre a base, e a combinação — e cada
/// combinação é literalmente a união de bits.
///
/// <para><see cref="Baseline"/> são as 11 features do E3.2 (as 9 grandezas contínuas de áudio mais duração e
/// explícito) e entra SEMPRE: é a linha de base fixa contra a qual o ganho de cada bloco é medido. Os blocos
/// adicionais existem para pagar o próprio custo — um bloco só permanece no campeão se seu ganho isolado de MAE
/// for ≥ 2% (regra do card), e é o <see cref="FeatureSetComparison"/> que aplica essa régua.</para>
/// </summary>
[Flags]
internal enum PopularityFeatureSet
{
    /// <summary>As 11 features do E3.2: âncora da medição, presente em toda combinação avaliada.</summary>
    Baseline = 0,

    /// <summary>
    /// Bloco A — as não-contínuas do áudio: <c>Key</c> (one-hot de 12), <c>Mode</c> (booleano) e
    /// <c>TimeSignature</c> (one-hot). Codificação por TIPO: nunca <c>Key</c> como inteiro ordenado.
    /// </summary>
    NonContinuousAudio = 1,

    /// <summary>Bloco B — gênero por faixa (<c>audio_features.Genre</c>), one-hot com bucket para ausente.</summary>
    Genre = 2
}

/// <summary>
/// Descreve um <see cref="PopularityFeatureSet"/> em termos legíveis e auditáveis: os nomes LÓGICOS das
/// features que ele publica (não as colunas one-hot expandidas) e um rótulo curto para a tabela comparativa.
///
/// <para>Os nomes lógicos são o que vai para <c>ModelVersion.Features</c> e para o <c>GET /api/model/current</c>
/// — o cliente precisa saber que "Genre" entrou, não ver 113 colunas <c>Genre.rock</c>. A expansão one-hot é
/// detalhe do pipeline do ML.NET, não do contrato.</para>
/// </summary>
internal static class PopularityFeatureSetDescriptor
{
    /// <summary>
    /// De qual coluna CODIFICADA do pipeline cada nome lógico veio. É o mapa que devolve o slot one-hot ao
    /// nome do bloco: <c>GenreEncoded.rock</c> pertence à feature lógica <c>Genre</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LogicalNamesByEncodedColumn =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PopularityModelPipeline.KeyEncodedColumn] = nameof(PopularityTrainingRow.Key),
            [PopularityModelPipeline.TimeSignatureEncodedColumn] = nameof(PopularityTrainingRow.TimeSignature),
            [PopularityModelPipeline.GenreEncodedColumn] = nameof(PopularityTrainingRow.Genre)
        };

    /// <summary>
    /// Os nomes lógicos das features do conjunto, na ordem em que entram no vetor: primeiro as 11 do E3.2,
    /// depois o Bloco A, depois o Bloco B. É a lista publicada como feature set da versão.
    /// </summary>
    public static IReadOnlyList<string> LogicalFeatureNames(PopularityFeatureSet featureSet)
    {
        var names = new List<string>(PopularityModelPipeline.BaselineFeatureColumns);

        if (featureSet.HasFlag(PopularityFeatureSet.NonContinuousAudio))
        {
            names.Add(nameof(PopularityTrainingRow.Key));
            names.Add(nameof(PopularityTrainingRow.Mode));
            names.Add(nameof(PopularityTrainingRow.TimeSignature));
        }

        if (featureSet.HasFlag(PopularityFeatureSet.Genre))
            names.Add(nameof(PopularityTrainingRow.Genre));

        return names;
    }

    /// <summary>
    /// Devolve o nome LÓGICO da feature a que um slot do vetor pertence (E3.6).
    ///
    /// <para>A concatenação do ML.NET nomeia cada slot como <c>{colunaDeOrigem}.{categoria}</c> quando a
    /// origem é um vetor one-hot, e simplesmente <c>{coluna}</c> quando é escalar. Basta então olhar o prefixo
    /// até o primeiro ponto — a categoria em si não interessa, e é justamente ela que transformaria a saída
    /// nas 113 colunas anônimas que o card proíbe.</para>
    /// </summary>
    public static string ResolveLogicalFeatureName(string slotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);

        int separator = slotName.IndexOf('.', StringComparison.Ordinal);
        string sourceColumn = separator < 0 ? slotName : slotName[..separator];

        return LogicalNamesByEncodedColumn.TryGetValue(sourceColumn, out string? logicalName)
            ? logicalName
            : sourceColumn;
    }

    /// <summary>Rótulo curto do conjunto para a tabela comparativa (ex.: <c>baseline</c>, <c>+A</c>, <c>A+B</c>).</summary>
    public static string Label(PopularityFeatureSet featureSet)
    {
        bool hasA = featureSet.HasFlag(PopularityFeatureSet.NonContinuousAudio);
        bool hasB = featureSet.HasFlag(PopularityFeatureSet.Genre);

        return (hasA, hasB) switch
        {
            (false, false) => "baseline",
            (true, false) => "+A",
            (false, true) => "+B",
            (true, true) => "A+B"
        };
    }
}
