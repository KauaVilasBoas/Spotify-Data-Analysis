using Microsoft.ML;
using Microsoft.ML.Data;
using SpotifyDataAnalysis.Modules.Prediction.Application.Training;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

// O ML.NET também tem um RegressionMetrics. O alias mantém explícito, em cada assinatura, qual dos dois está
// em jogo — o do domínio é o que atravessa a porta; o do framework morre aqui dentro.
using DomainMetrics = SpotifyDataAnalysis.Modules.Prediction.Domain.Training.RegressionMetrics;
using MlMetrics = Microsoft.ML.Data.RegressionMetrics;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;

/// <summary>
/// O pipeline de treino e avaliação do ML.NET, isolado de onde os dados vêm.
///
/// <para>Recebe <c>IDataView</c> pronto e devolve métricas, o que permite ao teste de gate treinar sobre uma
/// fixture determinística sem tocar no banco — teste de qualidade que depende do estado do catálogo é flaky
/// por construção.</para>
///
/// <para><b>E3.3:</b> o pipeline passou a ser parametrizado por <see cref="PopularityFeatureSet"/>. A mesma
/// máquina treina a base do E3.2, cada bloco isolado e a combinação, sobre o MESMO split/seed — é isso que
/// torna a medição comparativa honesta. O <see cref="PopularityFeatureSet.Baseline"/> reproduz bit a bit o
/// pipeline do E3.2; os blocos acrescentam transforms de codificação (one-hot / booleano), nunca substituem.</para>
/// </summary>
internal sealed class PopularityModelPipeline
{
    /// <summary>Nome do algoritmo campeão, ecoado no relatório.</summary>
    internal const string TrainerName = "FastTree";

    /// <summary>Folds da validação cruzada.</summary>
    internal const int CrossValidationFolds = 5;

    private const string FeaturesColumn = "Features";
    private const string LabelColumn = "Label";

    // Colunas intermediárias das codificações do E3.3. Nomes próprios (não sobrescrevem as colunas de origem)
    // para que a origem — Key/TimeSignature/Genre em texto — continue disponível e o schema fique auditável.
    private const string KeyEncodedColumn = "KeyEncoded";
    private const string TimeSignatureEncodedColumn = "TimeSignatureEncoded";
    private const string GenreEncodedColumn = "GenreEncoded";

    private readonly MLContext _mlContext;

    public PopularityModelPipeline(MLContext mlContext) => _mlContext = mlContext;

    /// <summary>
    /// As 11 features da linha de base (E3.2): as 9 grandezas contínuas de áudio mais duração e explícito. É o
    /// ponto de partida fixo da medição do E3.3 — o ganho de cada bloco é medido contra ele.
    /// </summary>
    internal static readonly string[] BaselineFeatureColumns =
    [
        nameof(PopularityTrainingRow.Danceability),
        nameof(PopularityTrainingRow.Energy),
        nameof(PopularityTrainingRow.Valence),
        nameof(PopularityTrainingRow.Tempo),
        nameof(PopularityTrainingRow.Acousticness),
        nameof(PopularityTrainingRow.Instrumentalness),
        nameof(PopularityTrainingRow.Liveness),
        nameof(PopularityTrainingRow.Speechiness),
        nameof(PopularityTrainingRow.Loudness),
        nameof(PopularityTrainingRow.DurationMs),
        nameof(PopularityTrainingRow.Explicit)
    ];

    /// <summary>
    /// Treina o campeão sobre a visão de treino, com o feature set pedido. A normalização não é enfeite:
    /// <c>Tempo</c> (BPM), <c>Loudness</c> (dB negativo) e <c>DurationMs</c> (centenas de milhares) conviveriam
    /// com features 0–1 e dominariam por escala.
    /// </summary>
    public ITransformer Train(IDataView trainingView, PopularityFeatureSet featureSet)
    {
        return BuildEstimator(
                _mlContext.Regression.Trainers.FastTree(labelColumnName: LabelColumn), featureSet)
            .Fit(trainingView);
    }

    /// <summary>Treina a regressão linear usada como segundo baseline, com o mesmo feature set.</summary>
    public ITransformer TrainLinearBaseline(IDataView trainingView, PopularityFeatureSet featureSet)
    {
        // Sdca e não Ols: o Ols do ML.NET vem em Microsoft.ML.Mkl.Components, que arrasta binário nativo — a
        // mesma razão pela qual o LightGbm foi descartado como campeão (DP-1).
        return BuildEstimator(_mlContext.Regression.Trainers.Sdca(labelColumnName: LabelColumn), featureSet)
            .Fit(trainingView);
    }

    /// <summary>Avalia um modelo treinado sobre a visão de TESTE.</summary>
    public DomainMetrics Evaluate(ITransformer model, IDataView testView)
    {
        IDataView predictions = model.Transform(testView);
        MlMetrics metrics = _mlContext.Regression.Evaluate(predictions, labelColumnName: LabelColumn);

        return new DomainMetrics(
            metrics.RSquared,
            metrics.MeanAbsoluteError,
            metrics.RootMeanSquaredError);
    }

    /// <summary>
    /// Métricas do baseline "prever sempre a média do TREINO". A média vem do treino, e não do teste, porque
    /// usar a média do próprio conjunto avaliado seria dar ao baseline uma informação que nenhum modelo tem em
    /// produção — e inflaria artificialmente o piso.
    /// </summary>
    public DomainMetrics EvaluateMeanBaseline(IDataView trainingView, IDataView testView)
    {
        double trainingMean = ReadLabels(trainingView).Average();

        return RegressionMetricsCalculator.CalculateForConstantPrediction(
            ReadLabels(testView), trainingMean);
    }

    /// <summary>
    /// Validação cruzada sobre o conjunto de TREINO, como leitura de estabilidade. O holdout dá um número; a
    /// dispersão entre folds diz se aquele número significa alguma coisa.
    /// </summary>
    public CrossValidationReport CrossValidate(IDataView trainingView, PopularityFeatureSet featureSet)
    {
        // Tipo do resultado é um genérico aninhado do framework (TrainCatalogBase.CrossValidationResult<>);
        // nomeá-lo aqui só acrescentaria ruído.
        var results =
            _mlContext.Regression.CrossValidate(
                trainingView,
                BuildEstimator(
                    _mlContext.Regression.Trainers.FastTree(labelColumnName: LabelColumn), featureSet),
                numberOfFolds: CrossValidationFolds,
                labelColumnName: LabelColumn);

        double[] rSquared = results.Select(result => result.Metrics.RSquared).ToArray();
        double[] meanAbsoluteError = results.Select(result => result.Metrics.MeanAbsoluteError).ToArray();

        return new CrossValidationReport(
            results.Count,
            rSquared.Average(),
            StandardDeviationOf(rSquared),
            meanAbsoluteError.Average(),
            StandardDeviationOf(meanAbsoluteError));
    }

    /// <summary>Quantas linhas a visão contém — o N que acompanha cada avaliação.</summary>
    public long CountRows(IDataView view) => ReadLabels(view).Count;

    /// <summary>
    /// Serializa o modelo no formato <c>.zip</c> do ML.NET, junto do schema de entrada. O schema viaja no
    /// artefato porque é ele que permite ao carregamento recusar um modelo incompatível em vez de predizer
    /// errado calado.
    /// </summary>
    public byte[] Serialize(ITransformer model, DataViewSchema inputSchema)
    {
        using var stream = new MemoryStream();
        _mlContext.Model.Save(model, inputSchema, stream);

        return stream.ToArray();
    }

    /// <summary>
    /// Monta o estimator para o feature set pedido. A estrutura é sempre a mesma — codificar categóricas,
    /// concatenar o vetor, normalizar, treinar —, e o feature set só decide QUAIS colunas entram na concatenação
    /// e quais codificações precedem. Blocos ausentes não deixam nenhum transform pendurado.
    /// </summary>
    private IEstimator<ITransformer> BuildEstimator(
        IEstimator<ITransformer> trainer, PopularityFeatureSet featureSet)
    {
        var featureColumns = new List<string>(BaselineFeatureColumns);

        // Encadeia as codificações num pipeline que pode começar vazio (baseline puro) e ganhar etapas por bloco.
        // O tipo é IEstimator<ITransformer> desde o início para que os Append condicionais componham sem cast.
        IEstimator<ITransformer>? encoding = null;

        if (featureSet.HasFlag(PopularityFeatureSet.NonContinuousAudio))
        {
            // Key e TimeSignature são CATEGÓRICAS, não ordinais: viajam como texto e viram one-hot. Mode é
            // binário e entra direto como 0/1 — one-hot de duas categorias só duplicaria a informação.
            encoding = Append(encoding, _mlContext.Transforms.Categorical.OneHotEncoding(
                KeyEncodedColumn, nameof(PopularityTrainingRow.KeyCategory)));
            encoding = Append(encoding, _mlContext.Transforms.Categorical.OneHotEncoding(
                TimeSignatureEncodedColumn, nameof(PopularityTrainingRow.TimeSignatureCategory)));

            featureColumns.Add(KeyEncodedColumn);
            featureColumns.Add(TimeSignatureEncodedColumn);
            featureColumns.Add(nameof(PopularityTrainingRow.Mode));
        }

        if (featureSet.HasFlag(PopularityFeatureSet.Genre))
        {
            encoding = Append(encoding, _mlContext.Transforms.Categorical.OneHotEncoding(
                GenreEncodedColumn, nameof(PopularityTrainingRow.GenreCategory)));

            featureColumns.Add(GenreEncodedColumn);
        }

        IEstimator<ITransformer> assembly =
            _mlContext.Transforms.Concatenate(FeaturesColumn, [.. featureColumns])
                .Append(_mlContext.Transforms.NormalizeMinMax(FeaturesColumn))
                .Append(trainer);

        return encoding is null ? assembly : encoding.Append(assembly);
    }

    private static IEstimator<ITransformer> Append(
        IEstimator<ITransformer>? head, IEstimator<ITransformer> next) =>
        head is null ? next : head.Append(next);

    private IReadOnlyList<double> ReadLabels(IDataView view) =>
        _mlContext.Data
            .CreateEnumerable<LabelOnlyRow>(view, reuseRowObject: false)
            .Select(row => (double)row.Label)
            .ToArray();

    /// <summary>
    /// Desvio-padrão populacional: os folds são a população completa da medição, não uma amostra dela.
    /// </summary>
    private static double StandardDeviationOf(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        double mean = values.Average();

        return Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / values.Count);
    }

    /// <summary>Projeção mínima para ler o alvo sem materializar as features.</summary>
    private sealed class LabelOnlyRow
    {
        [ColumnName(LabelColumn)]
        public float Label { get; set; }
    }
}
