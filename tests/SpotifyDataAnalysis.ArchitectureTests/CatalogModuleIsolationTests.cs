using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;

namespace SpotifyDataAnalysis.ArchitectureTests;

/// <summary>
/// Regras de isolamento do módulo <b>Catalog</b> (E0). Mesmas invariantes da seção "Arquitetura":
/// (c) o Domain do módulo é PURO — não referencia a Infrastructure compartilhada, EF Core, ASP.NET nem Dapper;
/// (b) a fronteira pública (Contracts) fica limpa — não depende de Domain/Application/Infrastructure do módulo
///     nem de EF Core, para que um consumidor que referencie só o Contracts não alcance os internos.
///
/// À medida que outros módulos surgirem, adiciona-se aqui a regra (a): Domain de um módulo não depende do
/// Domain de outro. <c>WithoutRequiringPositiveResults()</c> mantém as regras verdes enquanto um alvo ainda
/// não tem tipos avaliados.
/// </summary>
public sealed class CatalogModuleIsolationTests
{
    private static readonly Architecture Architecture = AssemblyRegistry.Architecture;

    private const string CatalogDomainAssembly = "SpotifyDataAnalysis.Modules.Catalog.Domain";
    private const string CatalogApplicationAssembly = "SpotifyDataAnalysis.Modules.Catalog.Application";
    private const string CatalogContractsAssembly = "SpotifyDataAnalysis.Modules.Catalog.Contracts";
    private const string CatalogInfrastructureAssembly = "SpotifyDataAnalysis.Modules.Catalog.Infrastructure";
    private const string SharedInfrastructureAssembly = "SpotifyDataAnalysis.Infrastructure";

    // ---- (c) Pureza do Domain do Catalog ----

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_SharedInfrastructure()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogDomainAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(SharedInfrastructureAssembly)
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_EntityFramework()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogDomainAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.EntityFrameworkCore")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_AspNetCore()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogDomainAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.AspNetCore")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_Dapper()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogDomainAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Dapper")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    // ---- (b) Limpeza da fronteira pública (Contracts) do Catalog ----

    [Fact]
    public void CatalogContracts_ShouldNotDependOn_CatalogDomain()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogContractsAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogDomainAssembly)
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogContracts_ShouldNotDependOn_CatalogApplication()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogContractsAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogApplicationAssembly)
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogContracts_ShouldNotDependOn_CatalogInfrastructure()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogContractsAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogInfrastructureAssembly)
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogContracts_ShouldNotDependOn_EntityFramework()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogContractsAssembly)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.EntityFrameworkCore")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }
}
