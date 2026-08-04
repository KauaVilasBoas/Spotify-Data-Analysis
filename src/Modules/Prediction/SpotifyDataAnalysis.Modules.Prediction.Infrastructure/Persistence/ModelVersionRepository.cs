using Microsoft.EntityFrameworkCore;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Persistence;

/// <summary>
/// Repositório EF das versões do modelo.
/// </summary>
internal sealed class ModelVersionRepository : IModelVersionRepository
{
    private readonly PredictionDbContext _dbContext;

    public ModelVersionRepository(PredictionDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task AddAsync(ModelVersion version, CancellationToken cancellationToken = default)
    {
        await _dbContext.ModelVersions.AddAsync(version, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<ModelVersion?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        _dbContext.ModelVersions
            .FirstOrDefaultAsync(version => version.Status == ModelVersionStatus.Current, cancellationToken);

    /// <inheritdoc />
    public async Task<ModelVersionSummary?> GetCurrentSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        // Projeção explícita SEM o artefato: a ficha de metadados não pode arrastar megabytes de binário.
        // O tamanho vem do banco por length(artifact), então continua informado sem transferir o conteúdo.
        var row = await _dbContext.ModelVersions
            .Where(version => version.Status == ModelVersionStatus.Current)
            .Select(version => new
            {
                version.Id,
                version.TrainedAtUtc,
                version.Trainer,
                version.Features,
                version.FeatureImportance,
                version.Seed,
                version.TestFraction,
                version.TrainingSampleCount,
                version.TestSampleCount,
                version.TrainedOnImputed,
                version.ModelMetrics,
                version.BaselineMetrics,
                version.ArtifactHash,
                ArtifactSizeBytes = (long)version.Artifact.Length
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        return new ModelVersionSummary(
            row.Id,
            row.TrainedAtUtc,
            row.Trainer,
            row.Features,
            row.FeatureImportance,
            row.Seed,
            row.TestFraction,
            row.TrainingSampleCount,
            row.TestSampleCount,
            row.TrainedOnImputed,
            row.ModelMetrics.RSquared,
            row.ModelMetrics.MeanAbsoluteError,
            row.ModelMetrics.RootMeanSquaredError,
            row.BaselineMetrics.RSquared,
            row.BaselineMetrics.MeanAbsoluteError,
            row.BaselineMetrics.RootMeanSquaredError,
            row.ArtifactHash,
            row.ArtifactSizeBytes);
    }

    /// <inheritdoc />
    public async Task PromoteAsync(ModelVersion version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        ModelVersion? previous = await _dbContext.ModelVersions
            .FirstOrDefaultAsync(
                candidate => candidate.Status == ModelVersionStatus.Current && candidate.Id != version.Id,
                cancellationToken);

        if (previous is null)
        {
            version.Promote();
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // Rebaixar e promover num único SaveChanges NÃO funciona: o índice único parcial é verificado a cada
        // statement, e o EF não garante emitir o UPDATE que rebaixa antes do que promove — quando promove
        // primeiro, existe um instante com duas correntes e o Postgres recusa com 23505 (um índice parcial não
        // pode ser DEFERRABLE, então não há como adiar a checagem).
        //
        // Por isso são dois SaveChanges em ORDEM EXPLÍCITA, dentro de uma transação: o par continua atômico
        // para quem observa de fora, e nenhum estado intermediário viola a invariante.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        previous.Demote();
        await _dbContext.SaveChangesAsync(cancellationToken);

        version.Promote();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
