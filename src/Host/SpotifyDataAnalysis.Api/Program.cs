using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using SpotifyDataAnalysis.Api.Middleware;
using SpotifyDataAnalysis.Api.Observability;
using SpotifyDataAnalysis.Infrastructure.DependencyInjection;
using SpotifyDataAnalysis.Infrastructure.Modules;
using SpotifyDataAnalysis.Jobs.DependencyInjection;
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
// Load order is determined by IModule.Order (lower = first). Modules are added
// to this array as each bounded context is created.
// ---------------------------------------------------------------------------
Assembly[] moduleAssemblies = [];

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
