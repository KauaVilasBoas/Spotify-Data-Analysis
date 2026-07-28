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
            typeof(Jobs.Scheduling.TimedBackgroundService).Assembly                // SpotifyDataAnalysis.Jobs
        )
        .Build();

    /// <summary>Arquitetura completa da solução, usada por todos os testes.</summary>
    public static Architecture Architecture => _architecture;
}
