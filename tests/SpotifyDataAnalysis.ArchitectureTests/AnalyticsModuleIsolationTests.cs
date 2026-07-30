using ClrAssembly = System.Reflection.Assembly;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;

namespace SpotifyDataAnalysis.ArchitectureTests;

/// <summary>
/// Regras de isolamento do módulo <b>Analytics</b> (E2). Mesmas invariantes do Catalog, MAIS a fronteira
/// específica deste módulo:
/// (c) o Domain do Analytics é PURO — não referencia a Infrastructure compartilhada, EF Core, ASP.NET nem Dapper;
/// (b) a fronteira pública (Contracts) fica limpa — não depende de Domain/Application/Infrastructure do módulo
///     nem de EF Core;
/// (a) <b>Analytics não referencia NENHUM código do Catalog</b> (Domain/Application/Infrastructure/Contracts):
///     o read-side de EDA lê o schema <c>catalog</c> por SQL/Dapper — acoplamento por DADO, não por código .NET.
///     É esta regra (não a ausência de referência de projeto) que sustenta a decisão DP-1: os ArchTests checam
///     dependências entre assemblies, e Analytics continua verde porque não toca em tipos do Catalog.
///
/// <para><b>Por que estes testes filtram por <see cref="ClrAssembly"/> e não pelo nome em string</b> (mesma
/// armadilha documentada em <see cref="HostBoundaryTests"/> e <see cref="CatalogModuleIsolationTests"/>): na
/// ArchUnitNET 0.13.3 a sobrecarga <c>ResideInAssembly(string)</c> compara com o <see cref="ClrAssembly.FullName"/>
/// (não com o nome simples), então um filtro por nome simples seleciona <b>zero</b> tipos e, como <b>subject</b>,
/// torna a regra vazia (falso-verde). A sobrecarga tipada <c>ResideInAssembly(ClrAssembly, params ClrAssembly[])</c>
/// não tem essa armadilha. Pelo mesmo motivo estas regras <b>não</b> usam <c>WithoutRequiringPositiveResults()</c>:
/// o subject de cada uma é um assembly concreto que sempre tem tipos; se ele sair da análise, o teste fica
/// vermelho em vez de silenciar. As RHS por namespace (EF Core/ASP.NET/Dapper) continuam por
/// <c>ResideInNamespace(...)</c>.</para>
/// </summary>
public sealed class AnalyticsModuleIsolationTests
{
    private static readonly Architecture Architecture = AssemblyRegistry.Architecture;

    private static readonly ClrAssembly AnalyticsDomain =
        typeof(Modules.Analytics.Domain.AnalyticsDomainAssemblyReference).Assembly;

    private static readonly ClrAssembly AnalyticsContracts =
        typeof(Modules.Analytics.Contracts.AnalyticsContractsAssemblyReference).Assembly;

    private static readonly ClrAssembly AnalyticsApplication =
        typeof(Modules.Analytics.Application.AnalyticsApplicationAssemblyReference).Assembly;

    private static readonly ClrAssembly AnalyticsInfrastructure =
        typeof(Modules.Analytics.Infrastructure.AnalyticsModule).Assembly;

    private static readonly ClrAssembly SharedInfrastructure =
        typeof(Infrastructure.Persistence.SpotifyDbContextBase).Assembly;

    // Assemblies do Catalog — alvos da fronteira "Analytics !-> Catalog".
    private static readonly ClrAssembly CatalogDomain =
        typeof(Modules.Catalog.Domain.CatalogDomainAssemblyReference).Assembly;

    private static readonly ClrAssembly CatalogContracts =
        typeof(Modules.Catalog.Contracts.CatalogContractsAssemblyReference).Assembly;

    private static readonly ClrAssembly CatalogApplication =
        typeof(Modules.Catalog.Application.CatalogApplicationAssemblyReference).Assembly;

    private static readonly ClrAssembly CatalogInfrastructure =
        typeof(Modules.Catalog.Infrastructure.CatalogModule).Assembly;

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
        AssertHasTypes(AnalyticsDomain);
        AssertHasTypes(AnalyticsContracts);
        AssertHasTypes(AnalyticsApplication);
        AssertHasTypes(AnalyticsInfrastructure);

        void AssertHasTypes(ClrAssembly assembly)
        {
            string simpleName = assembly.GetName().Name!;
            Assert.NotEmpty(Architecture.Types.Where(type => type.Assembly.Name == simpleName));
        }
    }

    // ---- (c) Pureza do Domain do Analytics ----

    [Fact]
    public void AnalyticsDomain_ShouldNotDependOn_SharedInfrastructure()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(AnalyticsDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(SharedInfrastructure);

        rule.Check(Architecture);
    }

    [Fact]
    public void AnalyticsDomain_ShouldNotDependOn_EntityFramework()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(AnalyticsDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.EntityFrameworkCore");

        rule.Check(Architecture);
    }

    [Fact]
    public void AnalyticsDomain_ShouldNotDependOn_AspNetCore()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(AnalyticsDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.AspNetCore");

        rule.Check(Architecture);
    }

    [Fact]
    public void AnalyticsDomain_ShouldNotDependOn_Dapper()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(AnalyticsDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Dapper");

        rule.Check(Architecture);
    }

    // ---- (b) Limpeza da fronteira pública (Contracts) do Analytics ----

    [Fact]
    public void AnalyticsContracts_ShouldNotDependOn_AnalyticsDomain()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(AnalyticsContracts)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(AnalyticsDomain);

        rule.Check(Architecture);
    }

    [Fact]
    public void AnalyticsContracts_ShouldNotDependOn_AnalyticsApplication()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(AnalyticsContracts)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(AnalyticsApplication);

        rule.Check(Architecture);
    }

    [Fact]
    public void AnalyticsContracts_ShouldNotDependOn_AnalyticsInfrastructure()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(AnalyticsContracts)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(AnalyticsInfrastructure);

        rule.Check(Architecture);
    }

    [Fact]
    public void AnalyticsContracts_ShouldNotDependOn_EntityFramework()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(AnalyticsContracts)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.EntityFrameworkCore");

        rule.Check(Architecture);
    }

    // ---- (a) Fronteira entre módulos: Analytics !-> Catalog (acoplamento é só o schema lido por SQL) ----

    /// <summary>
    /// A camada de aplicação do Analytics (onde vivem os QueryHandlers Dapper) NÃO pode depender de nenhum
    /// assembly do Catalog. O acoplamento permitido pela DP-1 é exclusivamente o schema <c>catalog</c> lido
    /// por SQL — nunca um tipo/projeto .NET do Catalog.
    /// </summary>
    [Fact]
    public void AnalyticsApplication_ShouldNotDependOn_AnyCatalogAssembly()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(AnalyticsApplication)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogDomain, CatalogContracts, CatalogApplication, CatalogInfrastructure);

        rule.Check(Architecture);
    }

    /// <summary>
    /// O composition root do Analytics também não pode alcançar o Catalog — mesma fronteira que a Application,
    /// para a camada mais externa do módulo não abrir uma porta lateral de acoplamento por código .NET.
    /// </summary>
    [Fact]
    public void AnalyticsInfrastructure_ShouldNotDependOn_AnyCatalogAssembly()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(AnalyticsInfrastructure)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogDomain, CatalogContracts, CatalogApplication, CatalogInfrastructure);

        rule.Check(Architecture);
    }

    /// <summary>
    /// Simétrica: o Catalog (módulo de escrita) não pode passar a depender do Analytics. Mantém a direção do
    /// acoplamento clara — Analytics lê o Catalog, nunca o contrário.
    /// </summary>
    [Fact]
    public void CatalogApplication_ShouldNotDependOn_AnyAnalyticsAssembly()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogApplication)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(AnalyticsDomain, AnalyticsContracts, AnalyticsApplication, AnalyticsInfrastructure);

        rule.Check(Architecture);
    }
}
