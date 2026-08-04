using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.ML;
using Microsoft.ML.Data;
using SpotifyDataAnalysis.Modules.Prediction.Application.Training;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

/// <summary>
/// Mede a importância das features do modelo campeão por PERMUTAÇÃO (E3.6): embaralha uma coluna do vetor de
/// cada vez sobre o conjunto de TESTE e observa quanto a qualidade da predição se degrada.
///
/// <para>Sobre o teste, e nunca sobre o treino: no treino a árvore já decorou o ruído, e a permutação mediria
/// o quanto ela se apoia na memorização, não o quanto a feature informa.</para>
///
/// <para>O resultado do ML.NET vem por SLOT do vetor — com o one-hot de gênero do E3.3 são mais de cem
/// colunas, uma por gênero. A tradução de slot para feature lógica fica com o
/// <see cref="PopularityFeatureSetDescriptor"/> e a soma por bloco com o domínio
/// (<see cref="FeatureImportanceRanking"/>); aqui só se mede.</para>
/// </summary>
internal sealed class PermutationFeatureImportanceCalculator
{
    /// <summary>
    /// Quantas vezes cada slot é embaralhado. Uma permutação só daria média sem dispersão — e o card exige a
    /// dispersão, porque um delta de uma amostra não distingue efeito de sorteio. Cinco é o menor número que
    /// produz um desvio-padrão com algum significado sem multiplicar o custo do treino.
    /// </summary>
    internal const int PermutationCount = 5;

    /// <summary>
    /// O ML.NET reporta a diferença como <c>métrica permutada − métrica original</c>, então o R² de uma
    /// feature importante vem NEGATIVO (o modelo piorou) e o MAE vem positivo. O ranking publicado é
    /// orientado no mesmo sentido para as duas métricas — maior sempre significa mais importante —, e é isso
    /// que estes sinais fazem: o R² é invertido, o MAE passa direto.
    /// </summary>
    private const double RSquaredOrientation = -1;

    /// <inheritdoc cref="RSquaredOrientation" />
    private const double MeanAbsoluteErrorOrientation = 1;

    private readonly MLContext _mlContext;

    public PermutationFeatureImportanceCalculator(MLContext mlContext) => _mlContext = mlContext;

    /// <summary>
    /// Mede a importância do modelo informado sobre a visão de teste e devolve o ranking já agregado por
    /// feature lógica, com o custo da medição registrado.
    /// </summary>
    public FeatureImportanceReport Measure(ITransformer model, IDataView testView)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(testView);

        long startedAt = Stopwatch.GetTimestamp();

        (ITransformer preprocessing, ISingleFeaturePredictionTransformer<object> predictor) = Split(model);
        IDataView featurized = preprocessing.Transform(testView);

        ImmutableArray<RegressionMetricsStatistics> perSlot =
            _mlContext.Regression.PermutationFeatureImportance(
                predictor,
                featurized,
                labelColumnName: PopularityModelPipeline.LabelColumn,
                permutationCount: PermutationCount);

        IReadOnlyList<string> slotNames = ReadSlotNames(featurized, perSlot.Length);

        IReadOnlyList<FeatureImportance> ranking = FeatureImportanceRanking.Build(
            perSlot.Select((statistics, slot) => ToSlotImportance(slotNames[slot], statistics)));

        return new FeatureImportanceReport(
            PermutationCount,
            perSlot.Length,
            ranking,
            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    /// <summary>
    /// Separa o modelo treinado em pré-processamento e preditor, porque é assim que a permutação do ML.NET
    /// pede o modelo: ela precisa embaralhar o vetor de features JÁ montado, e para isso o preditor tem de
    /// receber dados que as codificações e a normalização já atravessaram.
    ///
    /// <para>O achatamento é recursivo de propósito: o pipeline do E3.3 encadeia uma cadeia dentro de outra
    /// (as codificações one-hot antes da montagem), então o último transformer do topo é uma CADEIA, não o
    /// preditor — e a sobrecarga de conveniência da permutação rejeita exatamente esse formato.</para>
    /// </summary>
    private static (ITransformer Preprocessing, ISingleFeaturePredictionTransformer<object> Predictor) Split(
        ITransformer model)
    {
        ITransformer[] stages = Flatten(model).ToArray();

        if (stages[^1] is not ISingleFeaturePredictionTransformer<object> predictor)
            throw new InvalidOperationException(
                "O último estágio do modelo não é um preditor de feature única, e sem ele não há vetor de " +
                "features para permutar.");

        return (new TransformerChain<ITransformer>(stages[..^1]), predictor);
    }

    private static IEnumerable<ITransformer> Flatten(ITransformer transformer) =>
        transformer is IEnumerable<ITransformer> chain
            ? chain.SelectMany(Flatten)
            : [transformer];

    /// <summary>
    /// Os nomes de slot do vetor de features, que a concatenação anota no schema. É deles que sai o nome
    /// legível de cada linha do ranking; sem eles a saída seria uma lista de índices.
    /// </summary>
    private static IReadOnlyList<string> ReadSlotNames(IDataView featurized, int expectedCount)
    {
        DataViewSchema.Column features = featurized.Schema[PopularityModelPipeline.FeaturesColumn];

        VBuffer<ReadOnlyMemory<char>> slotNames = default;
        features.GetSlotNames(ref slotNames);

        string[] names = slotNames.DenseValues().Select(name => name.ToString()).ToArray();

        if (names.Length != expectedCount)
            throw new InvalidOperationException(
                $"O vetor de features tem {expectedCount} slots medidos, mas {names.Length} nomes no schema. " +
                "Publicar o ranking com os nomes desalinhados atribuiria a importância à feature errada.");

        return names;
    }

    private static FeatureSlotImportance ToSlotImportance(
        string slotName, RegressionMetricsStatistics statistics) =>
        new(
            PopularityFeatureSetDescriptor.ResolveLogicalFeatureName(slotName),
            new MetricDelta(
                RSquaredOrientation * statistics.RSquared.Mean,
                statistics.RSquared.StandardDeviation),
            new MetricDelta(
                MeanAbsoluteErrorOrientation * statistics.MeanAbsoluteError.Mean,
                statistics.MeanAbsoluteError.StandardDeviation));
}
