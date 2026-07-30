using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpotifyDataAnalysis.Infrastructure.Modules;
using SpotifyDataAnalysis.Modules.Prediction.Application;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.DependencyInjection;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure;

/// <summary>
/// Ponto de entrada (composition root) do módulo <b>Prediction</b> para o Composition Root do Host.
///
/// Como Catalog e Analytics, vive na Infrastructure — a camada mais externa do módulo — mantendo a direção de
/// dependência para dentro (Infrastructure → Application → Domain). O Host descobre este <see cref="IModule"/>
/// por varredura de assembly e NUNCA referencia o Domain interno (garantido pelos ArchitectureTests).
///
/// <para>Acoplamento consciente (DP-1): o Prediction LÊ o schema <c>catalog</c> via Dapper para montar o
/// dataset de treino. É acoplamento por DADO, não por código .NET — nenhum tipo/projeto do Catalog é
/// referenciado, e as regras de isolamento continuam verdes. Ao contrário do Analytics (read-only puro), o
/// Prediction terá write-side próprio a partir do E3.4, quando as versões de modelo passarem a ser
/// persistidas: é essa diferença de ciclo de vida que justifica um módulo, e não uma fatia dentro do
/// Analytics.</para>
///
/// <para>O ML.NET vive exclusivamente neste assembly, atrás da porta <c>ITrainingDatasetProvider</c>: para a
/// Application, treinar é um serviço externo como qualquer outro adaptador.</para>
/// </summary>
public sealed class PredictionModule : IModule
{
    /// <summary>
    /// Depois do Catalog (10) e do Analytics (20). Prediction consome o schema do Catalog e não contribui com
    /// serviços dos quais os outros dependam, então a ordem relativa é indiferente para a correção — mantida
    /// no fim apenas para leitura previsível do bootstrap.
    /// </summary>
    public int Order => 30;

    /// <summary>
    /// Registra os serviços do módulo. Os controllers MVC são co-locados na Application, então o assembly
    /// entra como <c>ApplicationPart</c> para o MVC do Host descobrir as actions.
    /// </summary>
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers().AddApplicationPart(typeof(PredictionApplicationAssemblyReference).Assembly);

        services.AddPredictionModule(configuration);
    }

    /// <summary>
    /// Sem endpoints de Minimal API: a superfície HTTP do módulo é exposta por controllers MVC co-locados na
    /// Application.
    /// </summary>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
