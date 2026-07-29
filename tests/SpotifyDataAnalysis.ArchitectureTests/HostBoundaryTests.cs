using ClrAssembly = System.Reflection.Assembly;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;

namespace SpotifyDataAnalysis.ArchitectureTests;

/// <summary>
/// Fronteira do <b>Host</b> (<c>SpotifyDataAnalysis.Api</c>) com os módulos (E0.1).
///
/// O Host referencia, por projeto, o <b>composition root</b> de cada módulo — o assembly Infrastructure, que
/// hospeda o <c>IModule</c> descoberto por varredura. Essa referência arrasta o Domain do módulo
/// transitivamente, então <b>não é a ausência da referência de projeto</b> que garante o isolamento: é a
/// regra abaixo. O Host compõe e expõe; nunca conhece agregados, value objects ou repositórios de um
/// bounded context.
///
/// <para><b>Por que estes testes filtram por <see cref="ClrAssembly"/> e não pelo nome em string:</b> na
/// ArchUnitNET 0.13.3 a sobrecarga <c>ResideInAssembly(string)</c> compara com o
/// <see cref="ClrAssembly.FullName"/> (<c>"Nome, Version=…, Culture=…, PublicKeyToken=…"</c>), e não com o nome
/// simples — um filtro por nome simples seleciona <b>zero</b> tipos e a regra passa vazia (falso-verde). A
/// sobrecarga tipada não tem essa armadilha. Pelo mesmo motivo estas regras <b>não</b> usam
/// <c>WithoutRequiringPositiveResults()</c>: se o assembly do Host sair da análise, o teste fica vermelho em
/// vez de silenciar.</para>
/// </summary>
public sealed class HostBoundaryTests
{
    private static readonly Architecture Architecture = AssemblyRegistry.Architecture;

    private static readonly ClrAssembly Host = AssemblyRegistry.HostAssembly;

    private static readonly ClrAssembly CatalogDomain =
        typeof(Modules.Catalog.Domain.CatalogDomainAssemblyReference).Assembly;

    /// <summary>
    /// Regra genérica: o Host não depende do Domain de <b>nenhum</b> módulo. Os alvos vêm de
    /// <see cref="AssemblyRegistry.ModuleDomainAssemblies"/>, então um bounded context novo passa a ser
    /// guardado assim que entra no registro — sem editar esta regra.
    /// </summary>
    [Fact]
    public void Api_ShouldNotDependOn_AnyModuleDomain()
    {
        ClrAssembly[] moduleDomains = [.. AssemblyRegistry.ModuleDomainAssemblies];

        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(Host)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(moduleDomains[0], moduleDomains[1..]);

        rule.Check(Architecture);
    }

    /// <summary>
    /// Impede que <see cref="Api_ShouldNotDependOn_AnyModuleDomain"/> vire um falso-verde: uma regra cujo
    /// alvo não casa nenhum assembly passa sem verificar nada. Aqui a descoberta por convenção é conferida
    /// contra o único módulo que existe hoje.
    /// </summary>
    [Fact]
    public void ModuleDomainAssemblies_ShouldBeDiscovered_SoTheGenericRuleIsNotVacuous()
        => Assert.Contains(CatalogDomain, AssemblyRegistry.ModuleDomainAssemblies);

    /// <summary>
    /// A mesma fronteira nomeando o Catalog explicitamente. Redundante com a regra genérica <b>de propósito</b>:
    /// é ela que documenta o único módulo existente hoje e que falha com uma mensagem legível se a descoberta
    /// por convenção deixar de enxergar um módulo (ex.: criado fora do padrão de nomes).
    /// </summary>
    [Fact]
    public void Api_ShouldNotDependOn_CatalogDomain()
    {
        IArchRule rule = ArchRuleDefinition
            .Types().That().ResideInAssembly(Host)
            .Should().NotDependOnAnyTypesThat()
            .ResideInAssembly(CatalogDomain);

        rule.Check(Architecture);
    }
}
