using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// Factory de design-time do <see cref="CatalogDbContext"/> — permite ao <c>dotnet ef</c> gerar migrations
/// sem um host rodando. Usa uma connection string descartável (só para construir o modelo; nunca alcança
/// produção — <c>migrations add</c> não conecta ao banco).
/// </summary>
internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CatalogDbContext> optionsBuilder =
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=spotify_design;Username=postgres;Password=postgres",
                    npgsql =>
                    {
                        npgsql.MigrationsHistoryTable("__ef_migrations_history", schema: "catalog");
                        npgsql.MigrationsAssembly(typeof(CatalogDbContextFactory).Assembly.GetName().Name);
                    });

        return new CatalogDbContext(optionsBuilder.Options);
    }
}
