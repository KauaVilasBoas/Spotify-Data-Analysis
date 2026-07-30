using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Persistence;

/// <summary>
/// Factory de design-time do <see cref="PredictionDbContext"/> — permite ao <c>dotnet ef</c> gerar e aplicar
/// migrations sem um host rodando, no mesmo padrão do Catalog (o projeto Api não referencia EFCore.Design e
/// por isso não serve de startup-project).
/// </summary>
internal sealed class PredictionDbContextFactory : IDesignTimeDbContextFactory<PredictionDbContext>
{
    public PredictionDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__SpotifyDb")
            ?? "Host=localhost;Database=spotify_design;Username=postgres;Password=postgres";

        DbContextOptionsBuilder<PredictionDbContext> optionsBuilder =
            new DbContextOptionsBuilder<PredictionDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsql =>
                    {
                        npgsql.MigrationsHistoryTable("__ef_migrations_history", schema: "prediction");
                        npgsql.MigrationsAssembly(typeof(PredictionDbContextFactory).Assembly.GetName().Name);
                    });

        return new PredictionDbContext(optionsBuilder.Options);
    }
}
