using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Infrastructure.AspNetCore;
using SpotifyDataAnalysis.Modules.Catalog.Application.Tracks;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Controllers;

/// <summary>
/// Endpoints de leitura do catálogo de faixas — a contraparte do <see cref="CatalogIngestionController"/>, que
/// só escreve. Controller fino: despacha a query e envelopa o caminho feliz; erros sobem para o error boundary
/// do Host.
/// </summary>
[Route("api/tracks")]
public sealed class TracksController : SpotifyControllerBase
{
    private readonly IMediator _mediator;

    public TracksController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Lista as faixas do catálogo com busca textual opcional, ordenação e paginação. <c>search</c> casa por
    /// substring sem diferenciar maiúsculas contra o nome da faixa ou o nome de qualquer artista creditado.
    /// <c>sort</c> aceita <c>Name</c> ou <c>PopularityDesc</c>, sempre com desempate pelo id da faixa para a
    /// paginação ser estável. <c>pageSize</c> é limitado a 200.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResult<PagedResult<TrackListItem>>), 200)]
    public async Task<ActionResult<ApiResult<PagedResult<TrackListItem>>>> SearchTracks(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] TrackSort sort = TrackSort.Name,
        CancellationToken cancellationToken = default)
    {
        var query = new SearchTracksQuery
        {
            Search = search,
            Page = page,
            PageSize = pageSize,
            Sort = sort
        };

        PagedResult<TrackListItem> result = await _mediator.SendAsync(query, cancellationToken);

        return Ok(new ApiResult<PagedResult<TrackListItem>>(true, "Faixas do catálogo.", result));
    }

    /// <summary>
    /// Detalhe de uma faixa. <c>audioFeatures</c> é nulo quando a faixa ainda não foi casada com o dataset
    /// externo e, quando presente, traz <c>isImputed</c> e <c>source</c> explícitos. Faixa inexistente responde
    /// 404 em ProblemDetails.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResult<TrackDetailResult>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<ApiResult<TrackDetailResult>>> GetTrackById(
        string id, CancellationToken cancellationToken)
    {
        TrackDetailResult result = await _mediator.SendAsync(new GetTrackByIdQuery(id), cancellationToken);

        return Ok(new ApiResult<TrackDetailResult>(true, "Detalhe da faixa.", result));
    }
}
