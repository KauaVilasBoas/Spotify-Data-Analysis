using ClrAssembly = System.Reflection.Assembly;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;

namespace SpotifyDataAnalysis.ArchitectureTests;

/// <summary>
/// Registro central dos assemblies analisados pelos testes de arquitetura.
/// ArchUnitNET carrega os assemblies uma única vez (lazy + cached) para performance.
///
/// À medida que os módulos (bounded contexts) forem criados, seus assemblies
/// (Domain/Application/Contracts/Infrastructure) são adicionados aqui, e as regras de
/// isolamento por módulo passam a avaliá-los.
/// </summary>
internal static class AssemblyRegistry
{
    /// <summary>
    /// Sufixo do assembly que carrega o <b>Domain</b> de um bounded context. É por ele que
    /// <see cref="ModuleDomainAssemblies"/> descobre os alvos das regras genéricas de fronteira, sem
    /// hardcodar módulo por módulo.
    /// </summary>
    private const string ModuleAssemblyPrefix = "SpotifyDataAnalysis.Modules.";
    private const string DomainAssemblySuffix = ".Domain";

    private static readonly ClrAssembly[] _analyzedAssemblies =
    [
        typeof(SharedKernel.Domain.Entity<Guid>).Assembly,                     // SpotifyDataAnalysis.SharedKernel
        typeof(Infrastructure.Persistence.SpotifyDbContextBase).Assembly,      // SpotifyDataAnalysis.Infrastructure
        typeof(Jobs.Scheduling.TimedBackgroundService).Assembly,               // SpotifyDataAnalysis.Jobs
        // Host: âncora num tipo público do Api (o Program é gerado por top-level statements e é interno).
        typeof(Api.Middleware.CorrelationIdMiddleware).Assembly,               // SpotifyDataAnalysis.Api
        // Catalog (E0): os 4 projetos do módulo, para as regras de isolamento avaliarem tipos reais.
        typeof(Modules.Catalog.Domain.CatalogDomainAssemblyReference).Assembly,          // Catalog.Domain
        typeof(Modules.Catalog.Contracts.CatalogContractsAssemblyReference).Assembly,    // Catalog.Contracts
        typeof(Modules.Catalog.Application.CatalogApplicationAssemblyReference).Assembly, // Catalog.Application
        typeof(Modules.Catalog.Infrastructure.CatalogModule).Assembly,                   // Catalog.Infrastructure
        // Analytics (E2): os 4 projetos do módulo, para as regras de isolamento avaliarem tipos reais.
        typeof(Modules.Analytics.Domain.AnalyticsDomainAssemblyReference).Assembly,          // Analytics.Domain
        typeof(Modules.Analytics.Contracts.AnalyticsContractsAssemblyReference).Assembly,    // Analytics.Contracts
        typeof(Modules.Analytics.Application.AnalyticsApplicationAssemblyReference).Assembly, // Analytics.Application
        typeof(Modules.Analytics.Infrastructure.AnalyticsModule).Assembly,                   // Analytics.Infrastructure
        // Prediction (E3): os 4 projetos do módulo, para as regras de isolamento avaliarem tipos reais.
        typeof(Modules.Prediction.Domain.PredictionDomainAssemblyReference).Assembly,          // Prediction.Domain
        typeof(Modules.Prediction.Contracts.PredictionContractsAssemblyReference).Assembly,    // Prediction.Contracts
        typeof(Modules.Prediction.Application.PredictionApplicationAssemblyReference).Assembly, // Prediction.Application
        typeof(Modules.Prediction.Infrastructure.PredictionModule).Assembly                    // Prediction.Infrastructure
    ];

    private static readonly Architecture _architecture = new ArchLoader()
        .LoadAssemblies(_analyzedAssemblies)
        .Build();

    /// <summary>Arquitetura completa da solução, usada por todos os testes.</summary>
    public static Architecture Architecture => _architecture;

    /// <summary>Assembly do Host (<c>SpotifyDataAnalysis.Api</c>).</summary>
    public static ClrAssembly HostAssembly { get; } = typeof(Api.Middleware.CorrelationIdMiddleware).Assembly;

    /// <summary>
    /// Assemblies de <b>Domain</b> de todos os módulos analisados, derivados por convenção de nome. Um
    /// bounded context novo entra automaticamente nas regras genéricas de fronteira assim que é adicionado
    /// a <see cref="_analyzedAssemblies"/> — sem editar as regras.
    /// </summary>
    public static IReadOnlyList<ClrAssembly> ModuleDomainAssemblies { get; } = _analyzedAssemblies
        .Where(assembly => assembly.GetName().Name is { } name
                           && name.StartsWith(ModuleAssemblyPrefix, StringComparison.Ordinal)
                           && name.EndsWith(DomainAssemblySuffix, StringComparison.Ordinal))
        .ToArray();
}
