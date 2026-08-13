using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using SpotifyDataAnalysis.Api.Middleware;
using SpotifyDataAnalysis.Api.Observability;
using SpotifyDataAnalysis.Infrastructure.DependencyInjection;
using SpotifyDataAnalysis.Infrastructure.Modules;
using SpotifyDataAnalysis.Jobs.DependencyInjection;
using SpotifyDataAnalysis.Modules.Analytics.Infrastructure;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure;
using SpotifyDataAnalysis.SharedKernel.Observability;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Structured logging — Serilog with the CorrelationId enricher (Console JSON).
// Replaces the default Microsoft logger.
// ---------------------------------------------------------------------------
builder.Host.UseSerilog(SerilogConfiguration.Configure);

// ---------------------------------------------------------------------------
// Shared infrastructure (lightweight mediator, IClock, DbConnectionFactory,
// CQRS logging/validation behaviors, correlation-id fallback accessor).
// ---------------------------------------------------------------------------
builder.Services.AddSpotifyInfrastructure();

// ---------------------------------------------------------------------------
// Request correlation. The Host owns the request-aware accessor, populated by
// CorrelationIdMiddleware from the X-Correlation-Id header; it overrides the
// ambient fallback registered by AddSpotifyInfrastructure. Registered as the
// concrete type too so the middleware can set the resolved id.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<CorrelationIdAccessor>();
builder.Services.Replace(
    ServiceDescriptor.Scoped<ICorrelationIdAccessor>(sp => sp.GetRequiredService<CorrelationIdAccessor>()));

// ---------------------------------------------------------------------------
// Composition Root — module discovery and registration via assembly scanning.
// Load order is determined by IModule.Order (lower = first). Each module's IModule
// lives in its Infrastructure (composition root) assembly. New modules are appended here.
// ---------------------------------------------------------------------------
Assembly[] moduleAssemblies =
[
    typeof(CatalogModule).Assembly,
    typeof(AnalyticsModule).Assembly,
    typeof(PredictionModule).Assembly,
];

ModuleLoader.RegisterModules(builder.Services, builder.Configuration, moduleAssemblies);

// ---------------------------------------------------------------------------
// MVC controllers contributed by the modules. Enums cross the API boundary as
// their NAME, not their ordinal, so names stay stable if an enum is reordered.
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---------------------------------------------------------------------------
// Background jobs — run in-process with the API. Registered AFTER the modules
// because the Outbox dispatcher job depends on the module-contributed
// IOutboxDbContext / write-side DbContext.
// ---------------------------------------------------------------------------
builder.Services.AddSpotifyJobs(builder.Configuration);

// ---------------------------------------------------------------------------
// Swagger / OpenAPI (development only)
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "SpotifyDataAnalysis API",
        Version = "v1",
        Description = "Spotify data analysis — .NET 8 modular monolith"
    });

    // Fully-qualified schema ids so two DTOs from different modules with the same simple name don't collide.
    options.CustomSchemaIds(type => type.FullName);

    options.MapType<DateOnly>(() => new Microsoft.OpenApi.Models.OpenApiSchema
        { Type = "string", Format = "date", Example = new Microsoft.OpenApi.Any.OpenApiString("2026-07-18") });
    options.MapType<TimeOnly>(() => new Microsoft.OpenApi.Models.OpenApiSchema
        { Type = "string", Format = "time", Example = new Microsoft.OpenApi.Any.OpenApiString("08:30:00") });
});

// ---------------------------------------------------------------------------
// Health Checks. The PostgreSQL health check reports 'Unhealthy' when the
// database is unavailable but does NOT block API startup.
// ---------------------------------------------------------------------------
string? pgConnectionString = builder.Configuration.GetConnectionString("SpotifyDb");

IHealthChecksBuilder healthChecks = builder.Services.AddHealthChecks();

if (!string.IsNullOrWhiteSpace(pgConnectionString))
{
    healthChecks.AddNpgSql(
        connectionString: pgConnectionString,
        name: "postgresql",
        tags: ["db", "ready"]);
}

// ---------------------------------------------------------------------------
// CORS for the SPA. Credentialed CORS is incompatible with the wildcard origin,
// so explicit origins allow credentials; no configured origins → AllowAnyOrigin
// without credentials (dev with a same-origin proxy).
// ---------------------------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("SpotifyCorsPolicy", policy =>
    {
        string[] allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        if (allowedOrigins.Length > 0)
        {
            policy
                .WithOrigins(allowedOrigins)
                .AllowCredentials();
        }
        else
        {
            policy.AllowAnyOrigin();
        }

        policy
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// ---------------------------------------------------------------------------
// ProblemDetails — RFC 7807 error contract. Registers the ProblemDetailsService
// used for framework-generated problems; the app's own ExceptionHandlingMiddleware
// writes the domain/application error shapes.
// ---------------------------------------------------------------------------
builder.Services.AddProblemDetails();

// ---------------------------------------------------------------------------
// Build
// ---------------------------------------------------------------------------
WebApplication app = builder.Build();

// ---------------------------------------------------------------------------
// Dev CLI (E1.10): `dotnet run -- seed-catalog [csvPath]` popula catalog.tracks a partir do dataset Kaggle
// (faixas + audio-features medidas + gênero) e ENCERRA — não sobe o servidor web nem os jobs. É carga de
// dados local (o catálogo não vem da API do Spotify neste projeto sem credenciais).
// ---------------------------------------------------------------------------
if (args.Length >= 1 && args[0] == "seed-catalog")
{
    string csvPath = args.Length >= 2 ? args[1] : "dataset.csv";
    using IServiceScope seedScope = app.Services.CreateScope();
    KaggleCatalogSeeder seeder = seedScope.ServiceProvider.GetRequiredService<KaggleCatalogSeeder>();

    CatalogSeedResult seed = await seeder.SeedAsync(csvPath);

    Console.WriteLine(
        $"[seed-catalog] {seed.Created} faixas criadas, {seed.DuplicatesSkipped} duplicadas, " +
        $"{seed.IncompleteSkipped} incompletas de {seed.TotalRows} linhas em {seed.ElapsedMilliseconds} ms.");
    return;
}

// ---------------------------------------------------------------------------
// Dev CLI (E1.11): `dotnet run -- seed-references [csvPath]` deriva artistas/álbuns dos NOMES do CSV e liga
// as faixas já semeadas pelo `seed-catalog` (créditos + album_id). Roda DEPOIS do seed-catalog e encerra.
// ---------------------------------------------------------------------------
if (args.Length >= 1 && args[0] == "seed-references")
{
    string csvPath = args.Length >= 2 ? args[1] : "dataset.csv";
    using IServiceScope referenceScope = app.Services.CreateScope();
    KaggleReferenceSeeder referenceSeeder =
        referenceScope.ServiceProvider.GetRequiredService<KaggleReferenceSeeder>();

    CatalogReferenceSeedResult references = await referenceSeeder.SeedAsync(csvPath);

    Console.WriteLine(
        $"[seed-references] {references.ArtistsCreated} artistas e {references.AlbumsCreated} álbuns criados, " +
        $"{references.TracksLinked} faixas ligadas ({references.TracksNotFound} não encontradas) de " +
        $"{references.TotalRows} linhas em {references.ElapsedMilliseconds} ms.");
    return;
}

// ---------------------------------------------------------------------------
// Dev CLI (E4.5): `dotnet run -- seed-playlists [pichlCsvPath] [datasetCsvPath] [maxPlaylists]` popula
// catalog.playlists a partir do dataset "Spotify Playlists" (Pichl et al.), casando cada faixa ao catálogo por
// TrackMatchKey reconstruído do dataset.csv. Dá o insumo de co-ocorrência do E4.6 e ENCERRA. Carga de dados
// local (a fonte é um CSV baixado manualmente, não a API do Spotify).
// ---------------------------------------------------------------------------
if (args.Length >= 1 && args[0] == "seed-playlists")
{
    string pichlCsvPath = args.Length >= 2 ? args[1] : Path.Combine("spotify-playlists", "spotify_dataset.csv");
    string datasetCsvPath = args.Length >= 3 ? args[2] : "dataset.csv";
    int maxPlaylists = args.Length >= 4 && int.TryParse(args[3], out int parsed) ? parsed : 50_000;

    using IServiceScope playlistScope = app.Services.CreateScope();
    PichlPlaylistSeeder playlistSeeder =
        playlistScope.ServiceProvider.GetRequiredService<PichlPlaylistSeeder>();

    PichlPlaylistSeedResult playlists =
        await playlistSeeder.SeedAsync(pichlCsvPath, datasetCsvPath, maxPlaylists);

    Console.WriteLine(
        $"[seed-playlists] {playlists.PlaylistsCreated} playlists criadas " +
        $"({playlists.PlaylistsSkippedEmpty} sem casamento, {playlists.PlaylistsSkippedExisting} já existentes) " +
        $"de {playlists.PlaylistsSeen} vistas; {playlists.RowsMatched}/{playlists.RowsRead} linhas casadas " +
        $"(taxa {playlists.MatchRate:P2}); {playlists.DistinctCatalogTracksCovered} faixas do catálogo cobertas; " +
        $"mapa {playlists.ResolvableMatchKeys} chaves ({playlists.AmbiguousMatchKeysDiscarded} ambíguas) " +
        $"em {playlists.ElapsedMilliseconds} ms.");
    return;
}

// ---------------------------------------------------------------------------
// HTTP pipeline
// ---------------------------------------------------------------------------

// First in the pipeline so it wraps every downstream middleware and endpoint,
// translating domain/application exceptions into the RFC 7807 ProblemDetails response.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Correlation id — resolves X-Correlation-Id (or generates one), stores it on the scoped accessor for the
// LoggingBehavior/ProblemDetails traceId, and echoes it on the response.
app.UseMiddleware<CorrelationIdMiddleware>();

// Security headers — a baseline of browser-hardening headers on every response.
app.UseMiddleware<SecurityHeadersMiddleware>();

// HSTS outside development only (never over plain HTTP in local dev).
if (!app.Environment.IsDevelopment())
    app.UseHsts();

// Serilog request logging — one structured summary line per request, enriched with the CorrelationId.
app.UseSerilogRequestLogging(options =>
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        diagnosticContext.Set(
            ObservabilityConstants.CorrelationIdProperty,
            httpContext.RequestServices.GetRequiredService<ICorrelationIdAccessor>().CorrelationId));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SpotifyDataAnalysis API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

app.UseCors("SpotifyCorsPolicy");

app.UseAuthorization();

// Health check endpoint — public.
app.MapHealthChecks("/health");

// Modules map their own Minimal API endpoints.
ModuleLoader.MapModuleEndpoints(app);

// MVC controllers from modules.
app.MapControllers();

app.Run();

// Required for xUnit WebApplicationFactory in future integration tests.
public partial class Program { }
