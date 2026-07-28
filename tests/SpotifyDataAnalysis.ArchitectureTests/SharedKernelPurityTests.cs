using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;

namespace SpotifyDataAnalysis.ArchitectureTests;

/// <summary>
/// Testes de arquitetura que garantem a PUREZA do SharedKernel — a fundação do monólito modular.
///
/// Regras invioláveis (seção "Arquitetura" do agente Dev):
/// (f) SharedKernel é puro — só abstrações e primitivos de domínio. NÃO depende de infraestrutura
///     (SpotifyDataAnalysis.Infrastructure), EF Core, ASP.NET Core nem Dapper.
///
/// As regras de ISOLAMENTO ENTRE MÓDULOS (Domain de um módulo não referencia Domain de outro;
/// comunicação só via *.Contracts; Host não referencia Domain interno) são adicionadas neste projeto
/// conforme cada bounded context ganha seus assemblies — ver <see cref="AssemblyRegistry"/>.
///
/// <c>WithoutRequiringPositiveResults()</c> mantém as regras verdes mesmo quando um alvo ainda não
/// tem tipos avaliados; remova-o conforme cada regra passar a ter avaliação positiva obrigatória.
/// </summary>
public sealed class SharedKernelPurityTests
{
    private static readonly Architecture Architecture = AssemblyRegistry.Architecture;

    private const string SharedKernelAssembly = "SpotifyDataAnalysis.SharedKernel";
    private const string InfrastructureAssembly = "SpotifyDataAnalysis.Infrastructure";

    /// <summary>(f) SharedKernel não deve depender de SpotifyDataAnalysis.Infrastructure.</summary>
    [Fact]
    public void SharedKernel_ShouldNotDependOn_Infrastructure()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(SharedKernelAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(InfrastructureAssembly)
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    /// <summary>(f) SharedKernel não deve depender de EF Core — permanece livre de persistência.</summary>
    [Fact]
    public void SharedKernel_ShouldNotDependOn_EntityFramework()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(SharedKernelAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.EntityFrameworkCore")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    /// <summary>(f) SharedKernel não deve depender de ASP.NET Core — não conhece o pipeline HTTP.</summary>
    [Fact]
    public void SharedKernel_ShouldNotDependOn_AspNetCore()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(SharedKernelAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.AspNetCore")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    /// <summary>(f) SharedKernel não deve usar Dapper — read-side é responsabilidade da Infrastructure/módulos.</summary>
    [Fact]
    public void SharedKernel_ShouldNotDependOn_Dapper()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(SharedKernelAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Dapper")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }
}
