using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Infrastructure.AspNetCore;
using SpotifyDataAnalysis.Modules.Analytics.Application.Insights;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Application.Controllers;

/// <summary>
/// Endpoints de leitura (insights/EDA) do módulo Analytics (E2). Controller fino: só despacha a query via
/// <see cref="IMediator"/> e envelopa o resultado em <see cref="ApiResult{T}"/> — nenhuma regra de negócio
/// aqui, nenhum acesso direto a DbContext/Dapper (o read-side vive nos QueryHandlers). Erros sobem para o
/// middleware de exceções do Host (RFC 7807).
/// </summary>
[Route("api/insights")]
public sealed class InsightsController : SpotifyControllerBase
{
    private readonly IMediator _mediator;

    public InsightsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Resumo do catálogo: total de faixas, cobertura de audio-features (com/sem, medidas vs imputadas) e contagens distintas de artistas, álbuns e gêneros.</summary>
    /// <remarks>
    /// As features imputadas (preenchidas por tratamento de faltantes) são reportadas SEPARADAS das medidas
    /// e nunca contadas como medidas, para o consumidor conhecer o N real por trás de qualquer recorte de EDA.
    /// </remarks>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResult<CatalogSummaryResult>), 200)]
    public async Task<ActionResult<ApiResult<CatalogSummaryResult>>> GetSummary(
        CancellationToken cancellationToken)
    {
        CatalogSummaryResult result =
            await _mediator.SendAsync(new GetCatalogSummaryQuery(), cancellationToken);

        return Ok(new ApiResult<CatalogSummaryResult>(true, "Resumo do catálogo.", result));
    }
}
