using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;
using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Exceptions;
using SpotifyDataAnalysis.SharedKernel.Guards;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Models;

/// <summary>
/// Uma versão treinada do modelo de popularidade: o artefato serializado somado a tudo que é preciso para
/// auditá-lo depois.
///
/// <para>Os metadados não são acessório: um <c>.zip</c> sem feature set, sem semente e sem as métricas do
/// baseline é lixo auditável — não dá para reproduzir o treino nem justificar uma predição. Por isso eles
/// entram na mesma gravação do binário.</para>
/// </summary>
public sealed class ModelVersion : AggregateRoot<int>
{
    private readonly List<string> _features;
    private readonly List<FeatureImportance> _featureImportance;

    private ModelVersion(
        DateTime trainedAtUtc,
        string trainer,
        List<string> features,
        List<FeatureImportance> featureImportance,
        int seed,
        double testFraction,
        long trainingSampleCount,
        long testSampleCount,
        bool trainedOnImputed,
        RegressionMetrics modelMetrics,
        RegressionMetrics baselineMetrics,
        byte[] artifact,
        string artifactHash) : base(default)
    {
        TrainedAtUtc = trainedAtUtc;
        Trainer = trainer;
        _features = features;
        _featureImportance = featureImportance;
        Seed = seed;
        TestFraction = testFraction;
        TrainingSampleCount = trainingSampleCount;
        TestSampleCount = testSampleCount;
        TrainedOnImputed = trainedOnImputed;
        ModelMetrics = modelMetrics;
        BaselineMetrics = baselineMetrics;
        Artifact = artifact;
        ArtifactHash = artifactHash;
        Status = ModelVersionStatus.Candidate;
    }

    // Construtor sem parâmetros para a materialização do EF Core; a hidratação sobrescreve tudo.
    private ModelVersion() : base(default)
    {
        Trainer = string.Empty;
        _features = [];
        _featureImportance = [];
        ModelMetrics = new RegressionMetrics(0, 0, 0);
        BaselineMetrics = new RegressionMetrics(0, 0, 0);
        Artifact = [];
        ArtifactHash = string.Empty;
    }

    /// <summary>Instante do treino (UTC).</summary>
    public DateTime TrainedAtUtc { get; private set; }

    /// <summary>Algoritmo campeão usado no treino.</summary>
    public string Trainer { get; private set; }

    /// <summary>
    /// Features que compuseram o vetor, na ordem. É o que permite recusar o modelo quando o pipeline mudar de
    /// feature set — carregar um modelo antigo com schema novo prediz errado em silêncio.
    /// </summary>
    public IReadOnlyList<string> Features => _features.AsReadOnly();

    /// <summary>
    /// O ranking de importância das features desta versão (E3.6), medido por permutação sobre o conjunto de
    /// TESTE no momento do treino e sempre em ordem decrescente de degradação.
    ///
    /// <para>Fica gravado com a versão, e não recalculado na leitura, porque a permutação reexecuta a
    /// avaliação uma vez por slot do vetor — custo de treino, jamais de request. Versões registradas antes do
    /// E3.6 têm a lista VAZIA, e isso é diferente de "nenhuma feature importa": é "não foi medido".</para>
    /// </summary>
    public IReadOnlyList<FeatureImportance> FeatureImportance => _featureImportance.AsReadOnly();

    /// <summary>Semente do split e do contexto de ML — a chave para reproduzir este treino.</summary>
    public int Seed { get; private set; }

    /// <summary>Fração reservada para teste.</summary>
    public double TestFraction { get; private set; }

    public long TrainingSampleCount { get; private set; }

    public long TestSampleCount { get; private set; }

    /// <summary>Se faixas com features imputadas entraram no treino desta versão.</summary>
    public bool TrainedOnImputed { get; private set; }

    /// <summary>Métricas do modelo no conjunto de teste.</summary>
    public RegressionMetrics ModelMetrics { get; private set; }

    /// <summary>
    /// Métricas do baseline de prever a média. Guardadas junto porque a métrica do modelo, sozinha, não diz
    /// se ele aprendeu — e sem elas a versão não é comparável com nenhuma outra.
    /// </summary>
    public RegressionMetrics BaselineMetrics { get; private set; }

    /// <summary>O <c>.zip</c> serializado do ML.NET.</summary>
    public byte[] Artifact { get; private set; }

    /// <summary>Hash do artefato — detecta corrupção ou troca do binário.</summary>
    public string ArtifactHash { get; private set; }

    /// <summary>Se esta versão é a que responde as predições, ou apenas uma candidata registrada.</summary>
    public ModelVersionStatus Status { get; private set; }

    /// <summary>Atalho de leitura para o estado de corrente.</summary>
    public bool IsCurrent => Status == ModelVersionStatus.Current;

    /// <summary>
    /// Registra uma versão recém-treinada. Nasce sempre CANDIDATA — promover é ato separado.
    ///
    /// <para>O ranking de importância entra pela ordenação do agregado, e não pela do chamador: "publicado em
    /// ordem decrescente de importância" é invariante da versão, e invariante que depende de quem chama não é
    /// invariante.</para>
    /// </summary>
    public static ModelVersion Register(
        DateTime trainedAtUtc,
        string trainer,
        IEnumerable<string> features,
        IEnumerable<FeatureImportance>? featureImportance,
        int seed,
        double testFraction,
        long trainingSampleCount,
        long testSampleCount,
        bool trainedOnImputed,
        RegressionMetrics modelMetrics,
        RegressionMetrics baselineMetrics,
        byte[] artifact,
        string artifactHash)
    {
        Guard.AgainstNullOrWhiteSpace(trainer, nameof(trainer));
        Guard.AgainstNullOrWhiteSpace(artifactHash, nameof(artifactHash));
        Guard.AgainstNull(modelMetrics, nameof(modelMetrics));
        Guard.AgainstNull(baselineMetrics, nameof(baselineMetrics));

        List<string> featureList = features?.ToList() ?? [];

        if (featureList.Count == 0)
            throw new DomainException(
                "Uma versão sem feature set não é auditável: não há como reproduzir o treino nem validar a " +
                "compatibilidade do modelo na carga.");

        if (artifact is null || artifact.Length == 0)
            throw new DomainException("Uma versão de modelo sem artefato serializado não serve para nada.");

        List<FeatureImportance> rankedImportance = featureImportance is null
            ? []
            : FeatureImportanceRanking.Order(featureImportance).ToList();

        return new ModelVersion(
            trainedAtUtc, trainer, featureList, rankedImportance, seed, testFraction, trainingSampleCount,
            testSampleCount, trainedOnImputed, modelMetrics, baselineMetrics, artifact, artifactHash);
    }

    /// <summary>Promove esta versão a corrente. Idempotente.</summary>
    public void Promote() => Status = ModelVersionStatus.Current;

    /// <summary>
    /// Rebaixa esta versão a candidata. Chamado ao promover outra — a unicidade de corrente é mantida pelo
    /// repositório, que rebaixa a anterior na MESMA transação, e garantida no banco por índice único parcial.
    /// </summary>
    public void Demote() => Status = ModelVersionStatus.Candidate;

    /// <summary>
    /// Se esta versão é compatível com o feature set informado. Um modelo carregado com um vetor de features
    /// diferente do que treinou não falha — ele prediz errado calado, que é bem pior.
    /// </summary>
    public bool IsCompatibleWith(IReadOnlyList<string> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        return _features.SequenceEqual(features, StringComparer.Ordinal);
    }
}

/// <summary>Estado de publicação de uma versão.</summary>
public enum ModelVersionStatus
{
    /// <summary>Registrada e auditável, mas não responde predições.</summary>
    Candidate,

    /// <summary>A versão que responde as predições. No máximo uma por vez.</summary>
    Current
}
