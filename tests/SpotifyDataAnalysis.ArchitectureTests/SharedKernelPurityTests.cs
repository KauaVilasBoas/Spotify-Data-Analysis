using ClrAssembly = System.Reflection.Assembly;
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
/// <para><b>Por que estes testes filtram por <see cref="ClrAssembly"/> e não pelo nome em string</b>
/// (mesma armadilha documentada em <see cref="HostBoundaryTests"/> e
/// <see cref="CatalogModuleIsolationTests"/>): na ArchUnitNET 0.13.3 a sobrecarga
/// <c>ResideInAssembly(string)</c> compara com o <see cref="ClrAssembly.FullName"/>
/// (<c>"Nome, Version=…, Culture=…, PublicKeyToken=…"</c>), e não com o nome simples — um filtro
/// por nome simples seleciona <b>zero</b> tipos e, como <b>subject</b>, torna a regra vazia
/// (falso-verde). A sobrecarga tipada <c>ResideInAssembly(ClrAssembly, params ClrAssembly[])</c>
/// não tem essa armadilha. Pelo mesmo motivo estas regras <b>não</b> usam
/// <c>WithoutRequiringPositiveResults()</c>: o subject (SharedKernel) é um assembly concreto que
/// sempre tem tipos; se ele sair da análise, o teste fica vermelho em vez de silenciar.</para>
/// </summary>
public sealed class SharedKernelPurityTests
{
    private static readonly Architecture Architecture = AssemblyRegistry.Architecture;

    private static readonly ClrAssembly SharedKernel =
        typeof(SpotifyDataAnalysis.SharedKernel.Domain.Entity<Guid>).Assembly;

    private static readonly ClrAssembly SharedInfrastructure =
        typeof(Infrastructure.Persistence.SpotifyDbContextBase).Assembly;

    // ---- Não-vacuidade: garante que o subject casa tipos reais (senão as regras passariam vazias) ----

    /// <summary>
    /// Impede que as regras deste arquivo virem falso-verde: uma regra cujo subject não casa nenhum
    /// tipo passa sem verificar nada. Aqui confirmamos que o SharedKernel entrou na análise com tipos.
    /// A comparação é pelo nome simples (<see cref="System.Reflection.AssemblyName.Name"/>), que é o
    /// que o ArchUnitNET expõe em <c>Assembly.Name</c>.
    /// </summary>
    [Fact]
    public void SharedKernelAssembly_ShouldHaveTypesInTheAnalysis_SoTheRulesAreNotVacuous()
    {
        string simpleName = SharedKernel.GetName().Name!;
        Assert.NotEmpty(Architecture.Types.Where(t => t.Assembly.Name == simpleName));
    }

    // ---- (f) Pureza do SharedKernel ----

    /// <summary>(f) SharedKernel não deve depender de SpotifyDataAnalysis.Infrastructure.</summary>
    [Fact]
    public void SharedKernel_ShouldNotDependOn_Infrastructure()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(SharedKernel)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(SharedInfrastructure);

        rule.Check(Architecture);
    }

    /// <summary>(f) SharedKernel não deve depender de EF Core — permanece livre de persistência.</summary>
    [Fact]
    public void SharedKernel_ShouldNotDependOn_EntityFramework()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(SharedKernel)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Microsoft.EntityFrameworkCore");

        rule.Check(Architecture);
    }

    /// <summary>(f) SharedKernel não deve depender de ASP.NET Core — não conhece o pipeline HTTP.</summary>
    [Fact]
    public void SharedKernel_ShouldNotDependOn_AspNetCore()
    {
        // ResideInNamespace faz correspondência exata de namespace. Como os tipos do ASP.NET Core residem
        // em sub-namespaces (Microsoft.AspNetCore.Http, Microsoft.AspNetCore.Mvc, etc.) e nunca no
        // namespace raiz "Microsoft.AspNetCore", é necessário usar ResideInNamespaceMatching com regex.
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(SharedKernel)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Microsoft\.AspNetCore(\..+)?");

        rule.Check(Architecture);
    }

    /// <summary>(f) SharedKernel não deve usar Dapper — read-side é responsabilidade da Infrastructure/módulos.</summary>
    [Fact]
    public void SharedKernel_ShouldNotDependOn_Dapper()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(SharedKernel)
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespace("Dapper");

        rule.Check(Architecture);
    }
}
