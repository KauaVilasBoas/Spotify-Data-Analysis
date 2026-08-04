namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Models;

/// <summary>
/// Persistência das versões do modelo. O binário viaja junto do agregado, e não por um store separado: é isso
/// que garante que metadados e artefato entrem na MESMA transação — um registro apontando para um binário que
/// não foi gravado seria pior que não ter registro.
/// </summary>
public interface IModelVersionRepository
{
    /// <summary>Registra uma nova versão.</summary>
    Task AddAsync(ModelVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// A versão corrente COM o artefato — usada no carregamento do modelo. Nula quando nada foi publicado.
    /// </summary>
    Task<ModelVersion?> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Os metadados da versão corrente, SEM o binário. É o que o <c>GET /api/model/current</c> devolve —
    /// arrastar megabytes de artefato para responder uma ficha de metadados seria desperdício por linha.
    /// </summary>
    Task<ModelVersionSummary?> GetCurrentSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Promove a versão informada e rebaixa a corrente anterior, na mesma unidade de trabalho — é aqui que a
    /// invariante "no máximo uma corrente" é mantida, com o índice único parcial do banco como rede final.
    /// </summary>
    Task PromoteAsync(ModelVersion version, CancellationToken cancellationToken = default);
}

/// <summary>
/// A ficha de uma versão, sem o artefato. Projeção de leitura para o endpoint e para diagnóstico.
/// </summary>
public sealed record ModelVersionSummary(
    int Version,
    DateTime TrainedAtUtc,
    string Trainer,
    IReadOnlyList<string> Features,
    IReadOnlyList<FeatureImportance> FeatureImportance,
    int Seed,
    double TestFraction,
    long TrainingSampleCount,
    long TestSampleCount,
    bool TrainedOnImputed,
    double ModelRSquared,
    double ModelMeanAbsoluteError,
    double ModelRootMeanSquaredError,
    double BaselineRSquared,
    double BaselineMeanAbsoluteError,
    double BaselineRootMeanSquaredError,
    string ArtifactHash,
    long ArtifactSizeBytes);
