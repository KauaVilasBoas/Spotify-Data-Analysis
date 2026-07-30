using ClrAssembly = System.Reflection.Assembly;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;

namespace SpotifyDataAnalysis.ArchitectureTests;

/// <summary>
/// Regras de isolamento do módulo <b>Catalog</b> (E0). Mesmas invariantes da seção "Arquitetura":
/// (c) o Domain do módulo é PURO — não referencia a Infrastructure compartilhada, EF Core, ASP.NET nem Dapper;
/// (b) a fronteira pública (Contracts) fica limpa — não depende de Domain/Application/Infrastructure do módulo
///     nem de EF Core, para que um consumidor que referencie só o Contracts não alcance os internos;
/// (d) os jobs de domínio despacham commands (a Application é o ponto de entrada público) mas não alcançam o
///     Domain/Infrastructure internos.
///
/// <para><b>Por que estes testes filtram por <see cref="ClrAssembly"/> e não pelo nome em string</b> (mesma
/// armadilha documentada em <see cref="HostBoundaryTests"/>): na ArchUnitNET 0.13.3 a sobrecarga
/// <c>ResideInAssembly(string)</c> compara com o <see cref="ClrAssembly.FullName"/>
/// (<c>"Nome, Version=…, Culture=…, PublicKeyToken=…"</c>), e não com o nome simples — um filtro por nome
/// simples seleciona <b>zero</b> tipos. Como <b>subject</b>, isso torna a regra vazia e, somado a
/// <c>WithoutRequiringPositiveResults()</c>, faz toda regra passar sem verificar nada (falso-verde). A
/// sobrecarga tipada <c>ResideInAssembly(ClrAssembly, params ClrAssembly[])</c> não tem essa armadilha, então
/// os subjects abaixo casam tipos reais. Os alvos vêm dos mesmos marker types do <see cref="AssemblyRegistry"/>.</para>
///
/// <para>Pelo mesmo motivo estas regras <b>não</b> usam <c>WithoutRequiringPositiveResults()</c>: o subject de
/// cada uma (Domain, Contracts, Jobs) é um assembly concreto que sempre tem tipos; se ele sair da análise, o
/// teste fica vermelho em vez de silenciar. As RHS por namespace (EF Core/ASP.NET/Dapper) continuam por
/// <c>ResideInNamespace(...)</c> — só o subject mudou para a sobrecarga tipada.</para>
/// </summary>
public sealed class CatalogModuleIsolationTests
{
    private static readonly Architecture Architecture = AssemblyRegistry.Architecture;

    private static readonly ClrAssembly CatalogDomain =
        typeof(Modules.Catalog.Domain.CatalogDomainAssemblyReference).Assembly;

    private static readonly ClrAssembly CatalogContracts =
        typeof(Modules.Catalog.Contracts.CatalogContractsAssemblyReference).Assembly;

    private static readonly ClrAssembly CatalogApplication =
        typeof(Modules.Catalog.Application.CatalogApplicationAssemblyReference).Assembly;

    private static readonly ClrAssembly CatalogInfrastructure =
        typeof(Modules.Catalog.Infrastructure.CatalogModule).Assembly;

    private static readonly ClrAssembly SharedInfrastructure =
        typeof(Infrastructure.Persistence.SpotifyDbContextBase).Assembly;

    private static readonly ClrAssembly Jobs =
        typeof(global::SpotifyDataAnalysis.Jobs.Scheduling.TimedBackgroundService).Assembly;

    // ---- Não-vacuidade: garante que os subjects casam tipos reais (senão as regras passariam vazias) ----

    /// <summary>
    /// Impede que as regras deste arquivo virem falso-verde: uma regra cujo subject não casa nenhum tipo passa
    /// sem verificar nada. Aqui confirmamos que cada assembly-subject usado abaixo entrou na análise com tipos.
    /// A comparação é pelo nome simples (<see cref="System.Reflection.AssemblyName.Name"/>), que é o que o
    /// ArchUnitNET expõe em <c>Assembly.Name</c>.
    /// </summary>
    [Fact]
    public void SubjectAssemblies_ShouldHaveTypesInTheAnalysis_SoTheRulesAreNotVacuous()
    {
        AssertHasTypes(CatalogDomain);
        AssertHasTypes(CatalogContracts);
        AssertHasTypes(Jobs);

        void AssertHasTypes(ClrAssembly assembly)
        {
            string simpleName = assembly.GetName().Name!;
            Assert.NotEmpty(Architecture.Types.Where(type => type.Assembly.Name == simpleName));
        }
    }

    // ---- (c) Pureza do Domain do Catalog ----

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_SharedInfrastructure()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(SharedInfrastructure);

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_EntityFramework()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.EntityFrameworkCore");

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_AspNetCore()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.AspNetCore");

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_Dapper()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Dapper");

        rule.Check(Architecture);
    }

    // ---- (b) Limpeza da fronteira pública (Contracts) do Catalog ----

    [Fact]
    public void CatalogContracts_ShouldNotDependOn_CatalogDomain()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogContracts)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogDomain);

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogContracts_ShouldNotDependOn_CatalogApplication()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogContracts)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogApplication);

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogContracts_ShouldNotDependOn_CatalogInfrastructure()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogContracts)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogInfrastructure);

        rule.Check(Architecture);
    }

    [Fact]
    public void CatalogContracts_ShouldNotDependOn_EntityFramework()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogContracts)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.EntityFrameworkCore");

        rule.Check(Architecture);
    }

    // ---- (d) Fronteira dos jobs de domínio (E1.6) ----

    /// <summary>
    /// Os jobs de domínio despacham COMMANDS do módulo (a Application é o ponto de entrada público), mas não
    /// podem alcançar o Domain interno — mesma fronteira que o Host respeita. A referência de projeto
    /// Jobs → Catalog.Application traz o Domain transitivamente, então é esta regra, e não a ausência da
    /// referência, que sustenta o isolamento.
    /// </summary>
    [Fact]
    public void Jobs_ShouldNotDependOn_CatalogDomain()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(Jobs)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogDomain);

        rule.Check(Architecture);
    }

    [Fact]
    public void Jobs_ShouldNotDependOn_CatalogInfrastructure()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(Jobs)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogInfrastructure);

        rule.Check(Architecture);
    }
}
