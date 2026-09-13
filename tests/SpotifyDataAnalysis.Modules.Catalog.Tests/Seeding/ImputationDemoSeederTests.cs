using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Persistence;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Seeding;

/// <summary>
/// <see cref="FactAttribute"/> que se auto-pula quando não há Postgres configurado.
/// Espelha o padrão do módulo Prediction (RecommenderEvaluationHarness.PostgresFactAttribute).
/// </summary>
public sealed class CatalogPostgresFactAttribute : FactAttribute
{
    public CatalogPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__SpotifyDb")))
            Skip = "Requer Postgres real: defina a variável de ambiente ConnectionStrings__SpotifyDb.";
    }
}

/// <summary>
/// Exercita o caminho de imputação de produção contra o Postgres real (E1.13).
///
/// <para>O que este teste prova:</para>
/// <list type="bullet">
///   <item>As faixas <c>imp_seed_*</c> são inseridas sem audio-features.</item>
///   <item>Após o seeder, todas têm <c>audio_features IS NOT NULL</c>.</item>
///   <item>Aquelas com features ausentes têm <c>is_imputed = true</c> no jsonb.</item>
///   <item>Nenhuma faixa preexistente (sem prefixo <c>imp_seed_</c>) mudou de estado.</item>
///   <item>Idempotência: segunda execução devolve as mesmas contagens.</item>
/// </list>
///
/// <para>Pulado automaticamente fora do ambiente local (sem <c>ConnectionStrings__SpotifyDb</c>).</para>
/// </summary>
public sealed class ImputationDemoSeederTests : IAsyncLifetime, IDisposable
{
    // A connection string vem da variável de ambiente, nunca do arquivo.
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__SpotifyDb")
        // Fallback local: mesmo valor dos User Secrets da API, só para o dev local que roda o teste
        // sem a variável — nunca gravado em arquivo versionado.
        ?? "Host=localhost;Port=5432;Database=spotify_data_analysis;Username=postgres;Password=admin";

    /// <summary>
    /// Os ids concretos gerados pelo seeder — usados para filtrar de forma traduzível pelo EF.
    /// O EF traduz Contains(list) para IN (...), que é portável e indexável.
    /// </summary>
    private static readonly List<SpotifyTrackId> DemoTrackIds =
    [
        SpotifyTrackId.Of("imp_seed_01"), SpotifyTrackId.Of("imp_seed_02"),
        SpotifyTrackId.Of("imp_seed_03"), SpotifyTrackId.Of("imp_seed_04"),
        SpotifyTrackId.Of("imp_seed_05"), SpotifyTrackId.Of("imp_seed_06"),
    ];

    private CatalogDbContext _db = null!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", schema: "catalog"))
            .Options;

        _db = new CatalogDbContext(options);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    public void Dispose() => _db?.Dispose();

    /// <summary>
    /// Apaga as faixas de demonstração antes/depois de cada execução.
    /// Equivale ao "procedimento de desfazer" documentado no card:
    /// <c>DELETE FROM catalog.tracks WHERE id IN ('imp_seed_01', ..., 'imp_seed_06')</c>
    /// </summary>
    private async Task CleanupDemoTracksAsync()
    {
        // EF traduz DemoTrackIds.Contains(t.Id) em IN (...) — traduzível e indexável.
        List<Track> existing = await _db.Tracks
            .Where(t => DemoTrackIds.Contains(t.Id))
            .ToListAsync();

        if (existing.Count > 0)
        {
            _db.Tracks.RemoveRange(existing);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [CatalogPostgresFact]
    public async Task SeedAsync_InsertsTracksAndAppliesRealImputer_AgainstLiveDatabase()
    {
        await CleanupDemoTracksAsync();

        try
        {
            // Captura o estado das faixas preexistentes ANTES do seed (não-regressão).
            long preexistingTotal = await _db.Tracks
                .LongCountAsync(t => !DemoTrackIds.Contains(t.Id));
            long preexistingImputed = await _db.Tracks
                .LongCountAsync(t => !DemoTrackIds.Contains(t.Id)
                    && t.AudioFeatures != null && t.AudioFeatures.IsImputed);

            string csvPath = FindKaggleCsv();
            var seeder = new ImputationDemoSeeder(_db, NullLogger<ImputationDemoSeeder>.Instance);

            // --- Primeira execução ---
            ImputationDemoSeedResult result1 = await seeder.SeedAsync(csvPath);

            Assert.Equal(6, result1.TracksInserted);
            Assert.Equal(0, result1.TracksAlreadyExisted);
            // Todas as 6 faixas têm pelo menos uma feature ausente → todas devem ser imputadas.
            Assert.Equal(6, result1.TracksProcessedByImputer);

            // Verifica no banco que as faixas têm audio_features e is_imputed = true.
            List<Track> seedTracks = await _db.Tracks
                .Where(t => DemoTrackIds.Contains(t.Id))
                .OrderBy(t => t.Id)
                .ToListAsync();

            Assert.Equal(6, seedTracks.Count);

            foreach (Track track in seedTracks)
            {
                Assert.NotNull(track.AudioFeatures);
                Assert.True(track.AudioFeatures!.IsImputed,
                    $"Faixa {track.Id.Value} deveria estar marcada como imputada.");
            }

            // --- Não-regressão: faixas preexistentes não mudaram ---
            long postTotal = await _db.Tracks
                .LongCountAsync(t => !DemoTrackIds.Contains(t.Id));
            long postImputed = await _db.Tracks
                .LongCountAsync(t => !DemoTrackIds.Contains(t.Id)
                    && t.AudioFeatures != null && t.AudioFeatures.IsImputed);

            Assert.Equal(preexistingTotal, postTotal);
            Assert.Equal(preexistingImputed, postImputed);

            // --- Idempotência: segunda execução devolve as mesmas contagens ---
            _db.ChangeTracker.Clear();
            ImputationDemoSeedResult result2 = await seeder.SeedAsync(csvPath);

            Assert.Equal(0, result2.TracksInserted);
            Assert.Equal(6, result2.TracksAlreadyExisted);
            // Na segunda execução o imputador re-processa as 6 existentes (comportamento idempotente);
            // TracksInserted=0 confirma que não houve duplicação.
            Assert.Equal(6, result2.TracksProcessedByImputer);
        }
        finally
        {
            // Garante limpeza mesmo em falha — nenhum caminho deixa dado para trás.
            await CleanupDemoTracksAsync();
        }
    }

    /// <summary>
    /// Localiza o CSV do Kaggle de audio-features. Procura na raiz do repositório
    /// (onde o dataset.csv vive durante o desenvolvimento local).
    /// </summary>
    private static string FindKaggleCsv()
    {
        // Sobe da pasta de saída do teste até encontrar o dataset.csv na raiz do repositório.
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "dataset.csv");
            if (File.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException(
            "dataset.csv não encontrado. Coloque o CSV do Kaggle na raiz do repositório.");
    }
}
