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
    private static readonly Architecture _architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(SharedKernel.Domain.Entity<Guid>).Assembly,                     // SpotifyDataAnalysis.SharedKernel
            typeof(Infrastructure.Persistence.SpotifyDbContextBase).Assembly,      // SpotifyDataAnalysis.Infrastructure
            typeof(Jobs.Scheduling.TimedBackgroundService).Assembly,               // SpotifyDataAnalysis.Jobs
            // Catalog (E0): os 4 projetos do módulo, para as regras de isolamento avaliarem tipos reais.
            typeof(Modules.Catalog.Domain.CatalogDomainAssemblyReference).Assembly,          // Catalog.Domain
            typeof(Modules.Catalog.Contracts.CatalogContractsAssemblyReference).Assembly,    // Catalog.Contracts
            typeof(Modules.Catalog.Application.CatalogApplicationAssemblyReference).Assembly, // Catalog.Application
            typeof(Modules.Catalog.Infrastructure.CatalogModule).Assembly                    // Catalog.Infrastructure
        )
        .Build();

    /// <summary>Arquitetura completa da solução, usada por todos os testes.</summary>
    public static Architecture Architecture => _architecture;
}
