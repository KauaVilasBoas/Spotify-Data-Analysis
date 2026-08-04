using Microsoft.EntityFrameworkCore;
using SpotifyDataAnalysis.Infrastructure.Persistence;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Prediction (write-side), no schema próprio <c>prediction</c> — é o estado que torna o
/// módulo um bounded context de verdade, e não um slice de leitura.
///
/// <para>Sem Outbox: uma versão de modelo publicada não é evento de negócio que outro módulo consuma. Se um
/// dia o E5 quiser reagir a "novo modelo publicado", aí entra o Outbox — não antes.</para>
/// </summary>
public sealed class PredictionDbContext : SpotifyDbContextBase
{
    public PredictionDbContext(DbContextOptions<PredictionDbContext> options) : base(options) { }

    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("prediction");

        modelBuilder.ApplyConfiguration(new ModelVersionConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
