using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Infrastructure.AspNetCore;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Controllers;

/// <summary>
/// Ponto de DIAGNÓSTICO INTERNO do recomendador content-based (E4.1) — o único que o card permite. Dada uma
/// faixa-semente do catálogo, devolve os vizinhos por cosseno sobre features normalizadas e a latência da
/// varredura, para o smoke test e a medição de latência p95 da DP-D.
///
/// <para>Não é o endpoint público de recomendação: este responde só ids + score (sem nomes, sem explicabilidade,
/// sem porquê rico), e mora em <c>api/internal/*</c> para deixar claro que é ferramenta de operação, não contrato
/// de produto. A superfície pública com explicabilidade é do E4.2; o filtro/boost por gênero, do E4.3.</para>
/// </summary>
[Route("api/internal/recommendations")]
public sealed class RecommendationsDiagnosticsController : SpotifyControllerBase
{
    private readonly IMediator _mediator;

    public RecommendationsDiagnosticsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Vizinhos mais parecidos com a faixa-semente, por cosseno sobre as nove audio-features normalizadas por
    /// z-score. Autoexclui a própria semente. Responde também o tamanho do índice e a latência da varredura em ms.
    /// </summary>
    /// <remarks>
    /// Semente inexistente ou sem as nove features completas → <c>seedFound=false</c> e <c>neighbors</c> vazio
    /// (não é um erro: é o diagnóstico dizendo que a faixa não entra no índice). Vizinhos imputados vêm marcados
    /// com <c>isImputed=true</c> (DP-F) — imputado nunca passa por medido em silêncio. <c>topN</c> é preso a
    /// [1, 100].
    /// </remarks>
    [HttpGet("{seedTrackId}/similar")]
    [ProducesResponseType(typeof(ApiResult<SimilarTracksResult>), 200)]
    public async Task<ActionResult<ApiResult<SimilarTracksResult>>> GetSimilarTracks(
        string seedTrackId,
        [FromQuery] int topN = GetSimilarTracksQuery.DefaultTopN,
        CancellationToken cancellationToken = default)
    {
        SimilarTracksResult result =
            await _mediator.SendAsync(new GetSimilarTracksQuery(seedTrackId, topN), cancellationToken);

        return Ok(new ApiResult<SimilarTracksResult>(
            true, "Faixas mais parecidas por similaridade de audio-features.", result));
    }
}
