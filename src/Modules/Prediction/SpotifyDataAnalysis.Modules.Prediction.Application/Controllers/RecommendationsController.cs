using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Infrastructure.AspNetCore;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Controllers;

/// <summary>
/// Endpoint PÚBLICO do recomendador content-based híbrido (E4.2/E4.3). Dada uma faixa-semente do catálogo, devolve
/// as N faixas mais parecidas por um score HÍBRIDO — cosseno sobre audio-features normalizadas combinado com a
/// afinidade de gênero da semente —, cada uma com o "porquê rico": score, as features que mais aproximaram (com
/// valores originais), o gênero compartilhado e a contribuição do gênero ao ranking.
///
/// <para>Controller fino: só monta a query, despacha via <see cref="IMediator"/> e envelopa em
/// <see cref="ApiResult{T}"/> — nenhuma regra de negócio, nenhum acesso a Dapper ou ao índice. Erros sobem para o
/// <c>ExceptionHandlingMiddleware</c> do Host (RFC 7807). É a superfície de produto; o diagnóstico interno (só
/// ids + score, sem explicabilidade) segue em <c>api/internal/recommendations</c>.</para>
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
    /// Ordena por score decrescente e autoexclui a própria semente. Cada recomendação traz o <c>score</c> HÍBRIDO,
    /// o <c>cosineScore</c> (só o áudio) e o <c>genreBoost</c> (quanto o gênero somou), as <c>topFeatures</c> que
    /// mais aproximaram a candidata da semente (com os valores ORIGINAIS de ambos os lados) e o <c>sharedGenre</c>
    /// quando compartilham o gênero.
    ///
    /// <para><b>Gênero no ranking (E4.3, DP-C):</b> por DEFAULT o gênero da semente PESA no ranking como um boost —
    /// candidatas do mesmo gênero ganham um bônus ADITIVO fixo de <b>0,05</b> no score
    /// (<c>score = cosineScore + genreBoost</c>), peso calibrado pelo E4.4 sobre o catálogo real. O boost NÃO
    /// exclui ninguém: só reordena, e uma candidata de outro gênero com cosine claramente melhor continua à frente.
    /// É relaxável por parâmetro: <c>genreMode=off</c> volta ao cosine puro de áudio, sem bônus algum (E4.1);
    /// <c>genreMode=boost</c> é o default descrito acima; <c>genreMode=sameGenreOnly</c> aplica o filtro DURO,
    /// descartando toda candidata de outro gênero antes do ranking (e, aí, sem bônus — a coerência já é garantida
    /// pela exclusão). O modo efetivo vem em <c>effectiveGenreMode</c>. Se a SEMENTE não tem
    /// gênero utilizável (ausente ou imputado, DP-F), o ranking cai graciosamente no cosine puro,
    /// <c>genreFellBackToCosineOnly=true</c> e há um aviso em <c>warnings</c> — nunca filtra para vazio em silêncio.</para>
    ///
    /// <para><b>Imputadas (DP-F):</b> uma recomendação com features imputadas vem marcada com
    /// <c>isImputed=true</c> e NÃO recebe boost de gênero (rótulo estimado não pontua); se a SEMENTE é imputada,
    /// <c>seedIsImputed=true</c> e há um aviso em <c>warnings</c> — imputado nunca passa por medido em silêncio.</para>
    ///
    /// <para><b>Erros:</b> <c>id</c> inexistente → 404 (RFC 7807); faixa existe mas SEM audio-features completas
    /// → 422 (não há vetor, logo não há como recomendar). <c>limit</c> fora de [1, 50], <c>explainTopK</c>
    /// fora de [1, 9] ou <c>genreMode</c> desconhecido → 400 (ProblemDetails).</para>
    ///
    /// <para><b>Exemplo de resposta (recortado):</b>
    /// <c>{ "success": true, "data": { "seedTrackId": "0e7ipj03S05BNilyu5bRzt", "seedName": "Nome da Semente",
    /// "seedGenre": "pop", "seedIsImputed": false, "indexedTrackCount": 89712,
    /// "requestedGenreMode": "boost", "effectiveGenreMode": "boost", "genreFellBackToCosineOnly": false,
    /// "recommendations": [ { "trackId": "1abc...", "name": "Faixa Parecida", "artist": "Artista", "album": "Álbum",
    /// "genre": "pop", "score": 1.031, "cosineScore": 0.981, "genreBoost": 0.05, "isImputed": false,
    /// "sharedGenre": "pop", "topFeatures": [
    /// { "feature": "Energy", "seedValue": 0.82, "candidateValue": 0.80, "contribution": 0.19 },
    /// { "feature": "Danceability", "seedValue": 0.74, "candidateValue": 0.73, "contribution": 0.17 },
    /// { "feature": "Valence", "seedValue": 0.66, "candidateValue": 0.64, "contribution": 0.15 } ] } ],
    /// "warnings": [] } }</c></para>
    /// </remarks>
    /// <param name="id">Id da faixa-semente no catálogo (Spotify track id).</param>
    /// <param name="limit">Quantas recomendações retornar. Padrão 10, máximo 50.</param>
    /// <param name="explainTopK">Quantas features destacar na explicação de cada recomendação. Padrão 3, máximo 9.</param>
    /// <param name="genreMode">Como o gênero da semente pesa no ranking. <c>boost</c> (DEFAULT): bônus aditivo de 0,05 no score das candidatas do mesmo gênero, sem excluir ninguém. <c>off</c>: cosine puro de audio-features, o gênero não pesa. <c>sameGenreOnly</c>: filtro duro, só candidatas do mesmo gênero concorrem. Valor desconhecido → 400.</param>
    /// <param name="dedupe">Se colapsa quase-duplicatas no top-N (E4.7). <c>true</c> (DEFAULT): a mesma música em <c>track_id</c>s diferentes vira UM item, e <c>equivalentVersionsCollapsed</c> conta as versões absorvidas; o representante é a de maior <c>popularity</c>. <c>false</c>: ranking cru, com duplicatas visíveis (debug).</param>
    /// <param name="strategy">Estratégia de recomendação (E4.6). <c>content</c> (DEFAULT): só áudio+gênero. <c>blend</c>: mistura o content-based com o sinal colaborativo item-item (faixas que co-ocorrem em playlists reais). No blend, cada item traz <c>signal</c> (content/collaborative/blended), <c>coPlaylists</c> e <c>coOccurrenceScore</c> (Jaccard). Se a semente não tem co-ocorrência, cai para content e avisa em <c>warnings</c>.</param>
    /// <param name="blendWeight">Peso do sinal colaborativo no blend, em [0, 1] (E4.6). Padrão 0,35. Só vale para <c>strategy=blend</c>. 0 = content puro; 1 = colaborativo puro.</param>
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
        [FromQuery] GenreRankingModeContract genreMode = GetTrackRecommendationsQuery.DefaultGenreMode,
        [FromQuery] bool dedupe = GetTrackRecommendationsQuery.DefaultDedupe,
        [FromQuery] RecommendationStrategyContract strategy = GetTrackRecommendationsQuery.DefaultStrategy,
        [FromQuery] double blendWeight = GetTrackRecommendationsQuery.DefaultBlendWeight,
        CancellationToken cancellationToken = default)
    {
        TrackRecommendationsResponse response = await _mediator.SendAsync(
            new GetTrackRecommendationsQuery(id, limit, explainTopK, genreMode, dedupe, strategy, blendWeight),
            cancellationToken);

        return Ok(new ApiResult<TrackRecommendationsResponse>(
            true, "Faixas recomendadas por similaridade híbrida (áudio + gênero).", response));
    }
}
