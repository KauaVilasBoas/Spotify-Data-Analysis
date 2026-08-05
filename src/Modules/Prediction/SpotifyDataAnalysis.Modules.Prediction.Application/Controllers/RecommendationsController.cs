using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Infrastructure.AspNetCore;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Controllers;

/// <summary>
/// Endpoint PÚBLICO do recomendador content-based (E4.2). Dada uma faixa-semente do catálogo, devolve as N faixas
/// mais parecidas por cosseno sobre audio-features normalizadas, cada uma com o "porquê rico": score, as features
/// que mais aproximaram (com valores originais) e o gênero compartilhado.
///
/// <para>Controller fino: só monta a query, despacha via <see cref="IMediator"/> e envelopa em
/// <see cref="ApiResult{T}"/> — nenhuma regra de negócio, nenhum acesso a Dapper ou ao índice. Erros sobem para o
/// <c>ExceptionHandlingMiddleware</c> do Host (RFC 7807). É a superfície de produto; o diagnóstico interno (só
/// ids + score, sem explicabilidade) segue em <c>api/internal/recommendations</c>, e o filtro/boost por gênero no
/// ranking é o E4.3.</para>
/// </summary>
[Route("api/recommendations")]
public sealed class RecommendationsController : SpotifyControllerBase
{
    private readonly IMediator _mediator;

    public RecommendationsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Faixas mais parecidas com a faixa-semente, por similaridade de audio-features, com explicabilidade.
    /// </summary>
    /// <remarks>
    /// Ordena por score (cosseno) decrescente e autoexclui a própria semente. Cada recomendação traz o
    /// <c>score</c>, as <c>topFeatures</c> que mais aproximaram a candidata da semente (com os valores ORIGINAIS
    /// de ambos os lados) e o <c>sharedGenre</c> quando semente e candidata compartilham o gênero — o gênero
    /// aqui é só explicação, não muda a ordem (isso é o E4.3).
    ///
    /// <para><b>Imputadas (DP-F):</b> uma recomendação com features imputadas vem marcada com
    /// <c>isImputed=true</c>; se a SEMENTE é imputada, <c>seedIsImputed=true</c> e há um aviso em
    /// <c>warnings</c> — imputado nunca passa por medido em silêncio.</para>
    ///
    /// <para><b>Erros:</b> <c>id</c> inexistente → 404 (RFC 7807); faixa existe mas SEM audio-features completas
    /// → 422 (não há vetor, logo não há como recomendar). <c>limit</c> fora de [1, 50] ou <c>explainTopK</c>
    /// fora de [1, 9] → 400 (ProblemDetails).</para>
    ///
    /// <para><b>Exemplo de resposta (recortado):</b>
    /// <c>{ "success": true, "data": { "seedTrackId": "0e7ipj03S05BNilyu5bRzt", "seedName": "Nome da Semente",
    /// "seedGenre": "pop", "seedIsImputed": false, "indexedTrackCount": 89712, "recommendations": [
    /// { "trackId": "1abc...", "name": "Faixa Parecida", "artist": "Artista", "album": "Álbum",
    /// "genre": "pop", "score": 0.981, "isImputed": false, "sharedGenre": "pop", "topFeatures": [
    /// { "feature": "Energy", "seedValue": 0.82, "candidateValue": 0.80, "contribution": 0.19 },
    /// { "feature": "Danceability", "seedValue": 0.74, "candidateValue": 0.73, "contribution": 0.17 },
    /// { "feature": "Valence", "seedValue": 0.66, "candidateValue": 0.64, "contribution": 0.15 } ] } ],
    /// "warnings": [] } }</c></para>
    /// </remarks>
    /// <param name="id">Id da faixa-semente no catálogo (Spotify track id).</param>
    /// <param name="limit">Quantas recomendações retornar. Padrão 10, máximo 50.</param>
    /// <param name="explainTopK">Quantas features destacar na explicação de cada recomendação. Padrão 3, máximo 9.</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    [HttpGet("track/{id}")]
    [ProducesResponseType(typeof(ApiResult<TrackRecommendationsResponse>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<ApiResult<TrackRecommendationsResponse>>> GetTrackRecommendations(
        string id,
        [FromQuery] int limit = GetTrackRecommendationsQuery.DefaultLimit,
        [FromQuery] int explainTopK = GetTrackRecommendationsQuery.DefaultExplainTopK,
        CancellationToken cancellationToken = default)
    {
        TrackRecommendationsResponse response = await _mediator.SendAsync(
            new GetTrackRecommendationsQuery(id, limit, explainTopK), cancellationToken);

        return Ok(new ApiResult<TrackRecommendationsResponse>(
            true, "Faixas recomendadas por similaridade de audio-features.", response));
    }
}
