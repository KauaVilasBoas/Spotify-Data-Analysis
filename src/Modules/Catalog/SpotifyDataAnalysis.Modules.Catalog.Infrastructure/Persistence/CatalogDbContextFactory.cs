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
        // `migrations add` não conecta no banco (o placeholder basta para construir o modelo).
        // `database update` conecta: aí lê a connection string real da variável de ambiente
        // ConnectionStrings__SpotifyDb (ou usa o placeholder como fallback).
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__SpotifyDb")
            ?? "Host=localhost;Database=spotify_design;Username=postgres;Password=postgres";

        DbContextOptionsBuilder<CatalogDbContext> optionsBuilder =
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsql =>
                    {
                        npgsql.MigrationsHistoryTable("__ef_migrations_history", schema: "catalog");
                        npgsql.MigrationsAssembly(typeof(CatalogDbContextFactory).Assembly.GetName().Name);
                    });

        return new CatalogDbContext(optionsBuilder.Options);
    }
}
