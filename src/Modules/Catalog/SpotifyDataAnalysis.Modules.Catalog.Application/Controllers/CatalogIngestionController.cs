using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Infrastructure.AspNetCore;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Controllers;

/// <summary>
/// Endpoints de ingestão do catálogo (E1.7). Só despacham commands via <see cref="IMediator"/> — nenhuma
/// regra de negócio aqui. O caminho feliz é envelopado em <see cref="ApiResult{T}"/>; erros de
/// domínio/aplicação sobem para o middleware de exceções do Host (RFC 7807).
/// </summary>
[Route("api/ingest")]
public sealed class CatalogIngestionController : SpotifyControllerBase
{
    private readonly IMediator _mediator;

    public CatalogIngestionController(IMediator mediator) => _mediator = mediator;

    /// <summary>Ingesta (coleta) as faixas de uma playlist-semente do Spotify no catálogo.</summary>
    [HttpPost("playlist/{playlistId}")]
    public async Task<ActionResult<ApiResult<IngestPlaylistResult>>> IngestPlaylist(
        string playlistId, CancellationToken cancellationToken)
    {
        IngestPlaylistResult result =
            await _mediator.SendAsync(new IngestPlaylistCommand(playlistId), cancellationToken);

        return Ok(new ApiResult<IngestPlaylistResult>(true, "Playlist ingerida no catálogo.", result));
    }
}
