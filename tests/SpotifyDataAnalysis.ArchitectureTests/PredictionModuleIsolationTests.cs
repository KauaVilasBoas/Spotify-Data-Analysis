using ClrAssembly = System.Reflection.Assembly;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;

namespace SpotifyDataAnalysis.ArchitectureTests;

/// <summary>
/// Regras de isolamento do módulo <b>Prediction</b> (E3). Mesmas invariantes do Catalog/Analytics, MAIS a
/// fronteira específica deste módulo:
/// (c) o Domain do Prediction é PURO — não referencia a Infrastructure compartilhada, EF Core, ASP.NET nem Dapper;
/// (b) a fronteira pública (Contracts) fica limpa — não depende de Domain/Application/Infrastructure do módulo
///     nem de EF Core;
/// (a) <b>Prediction não referencia NENHUM código do Catalog</b> (Domain/Application/Infrastructure/Contracts):
///     monta o dataset de treino lendo o schema <c>catalog</c> por SQL/Dapper — acoplamento por DADO, não por
///     código .NET (DP-1 do E3.1). O ML.NET vive só na Infrastructure e não é fronteira aqui.
///
/// <para>Filtra por <see cref="ClrAssembly"/> e não por nome em string (mesma armadilha documentada em
/// <see cref="HostBoundaryTests"/>/<see cref="AnalyticsModuleIsolationTests"/>): na ArchUnitNET 0.13.3 o
/// <c>ResideInAssembly(string)</c> compara com o FullName, então um nome simples seleciona zero tipos e, como
/// subject, torna a regra vazia (falso-verde). A sobrecarga tipada não tem essa armadilha, e por isso estas
/// regras também <b>não</b> usam <c>WithoutRequiringPositiveResults()</c>.</para>
/// </summary>
public sealed class PredictionModuleIsolationTests
{
    private static readonly Architecture Architecture = AssemblyRegistry.Architecture;

    private static readonly ClrAssembly PredictionDomain =
        typeof(Modules.Prediction.Domain.PredictionDomainAssemblyReference).Assembly;

    private static readonly ClrAssembly PredictionContracts =
        typeof(Modules.Prediction.Contracts.PredictionContractsAssemblyReference).Assembly;

    private static readonly ClrAssembly PredictionApplication =
        typeof(Modules.Prediction.Application.PredictionApplicationAssemblyReference).Assembly;

    private static readonly ClrAssembly PredictionInfrastructure =
        typeof(Modules.Prediction.Infrastructure.PredictionModule).Assembly;

    private static readonly ClrAssembly SharedInfrastructure =
        typeof(Infrastructure.Persistence.SpotifyDbContextBase).Assembly;

    // Assemblies do Catalog — alvos da fronteira "Prediction !-> Catalog".
    private static readonly ClrAssembly CatalogDomain =
        typeof(Modules.Catalog.Domain.CatalogDomainAssemblyReference).Assembly;

    private static readonly ClrAssembly CatalogContracts =
        typeof(Modules.Catalog.Contracts.CatalogContractsAssemblyReference).Assembly;

    private static readonly ClrAssembly CatalogApplication =
        typeof(Modules.Catalog.Application.CatalogApplicationAssemblyReference).Assembly;

    private static readonly ClrAssembly CatalogInfrastructure =
        typeof(Modules.Catalog.Infrastructure.CatalogModule).Assembly;

    // ---- Não-vacuidade: garante que os subjects casam tipos reais (senão as regras passariam vazias) ----

    [Fact]
    public void SubjectAssemblies_ShouldHaveTypesInTheAnalysis_SoTheRulesAreNotVacuous()
    {
        AssertHasTypes(PredictionDomain);
        AssertHasTypes(PredictionContracts);
        AssertHasTypes(PredictionApplication);
        AssertHasTypes(PredictionInfrastructure);

        void AssertHasTypes(ClrAssembly assembly)
        {
            string simpleName = assembly.GetName().Name!;
            Assert.NotEmpty(Architecture.Types.Where(type => type.Assembly.Name == simpleName));
        }
    }

    // ---- (c) Pureza do Domain do Prediction ----

    [Fact]
    public void PredictionDomain_ShouldNotDependOn_SharedInfrastructure()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(PredictionDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(SharedInfrastructure);

        rule.Check(Architecture);
    }

    [Fact]
    public void PredictionDomain_ShouldNotDependOn_EntityFramework()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(PredictionDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.EntityFrameworkCore");

        rule.Check(Architecture);
    }

    [Fact]
    public void PredictionDomain_ShouldNotDependOn_AspNetCore()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(PredictionDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.AspNetCore");

        rule.Check(Architecture);
    }

    [Fact]
    public void PredictionDomain_ShouldNotDependOn_Dapper()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(PredictionDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Dapper");

        rule.Check(Architecture);
    }

    [Fact]
    public void PredictionDomain_ShouldNotDependOn_MachineLearning()
    {
        // O ML.NET é um adaptador de tecnologia: mora na Infrastructure, atrás da porta ITrainingDatasetProvider.
        // O domínio entrega listas tipadas e não conhece o framework de ML.
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(PredictionDomain)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.ML");

        rule.Check(Architecture);
    }

    // ---- (b) Limpeza da fronteira pública (Contracts) do Prediction ----

    [Fact]
    public void PredictionContracts_ShouldNotDependOn_PredictionDomain()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(PredictionContracts)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(PredictionDomain);

        rule.Check(Architecture);
    }

    [Fact]
    public void PredictionContracts_ShouldNotDependOn_PredictionApplication()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(PredictionContracts)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(PredictionApplication);

        rule.Check(Architecture);
    }

    [Fact]
    public void PredictionContracts_ShouldNotDependOn_PredictionInfrastructure()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(PredictionContracts)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(PredictionInfrastructure);

        rule.Check(Architecture);
    }

    // ---- (a) Fronteira entre módulos: Prediction !-> Catalog (acoplamento é só o schema lido por SQL) ----

    /// <summary>
    /// A camada de aplicação do Prediction (onde vive a leitura Dapper do dataset) NÃO pode depender de nenhum
    /// assembly do Catalog. O acoplamento permitido pela DP-1 é exclusivamente o schema <c>catalog</c> lido por
    /// SQL — nunca um tipo/projeto .NET do Catalog.
    /// </summary>
    [Fact]
    public void PredictionApplication_ShouldNotDependOn_AnyCatalogAssembly()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(PredictionApplication)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogDomain, CatalogContracts, CatalogApplication, CatalogInfrastructure);

        rule.Check(Architecture);
    }

    [Fact]
    public void PredictionInfrastructure_ShouldNotDependOn_AnyCatalogAssembly()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(PredictionInfrastructure)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogDomain, CatalogContracts, CatalogApplication, CatalogInfrastructure);

        rule.Check(Architecture);
    }

    /// <summary>Simétrica: o Catalog (módulo de escrita) não pode passar a depender do Prediction.</summary>
    [Fact]
    public void CatalogApplication_ShouldNotDependOn_AnyPredictionAssembly()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(CatalogApplication)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(PredictionDomain, PredictionContracts, PredictionApplication, PredictionInfrastructure);

        rule.Check(Architecture);
    }
}
