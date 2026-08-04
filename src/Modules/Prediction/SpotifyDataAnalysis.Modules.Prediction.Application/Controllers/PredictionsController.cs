using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Infrastructure.AspNetCore;
using SpotifyDataAnalysis.Modules.Prediction.Application.Dataset;
using SpotifyDataAnalysis.Modules.Prediction.Application.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Inference;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Controllers;

/// <summary>
/// Endpoints do módulo Prediction (E3). Controller fino: só despacha a query via <see cref="IMediator"/> e
/// envelopa o resultado em <see cref="ApiResult{T}"/> — nenhuma regra de negócio aqui, nenhum acesso direto a
/// Dapper ou ao ML.NET. Erros sobem para o middleware de exceções do Host (RFC 7807).
/// </summary>
[Route("api/predictions")]
public sealed class PredictionsController : SpotifyControllerBase
{
    private readonly IMediator _mediator;

    public PredictionsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Diagnóstico do dataset de treino: com quantas faixas dá para treinar e quantas ficaram de fora, por
    /// motivo. Responde o total do catálogo, as exclusões (sem popularidade, sem audio-features,
    /// audio-features incompletas), os elegíveis com features medidas e com features imputadas, quantos foram
    /// descartados pela política de imputação, e os tamanhos de treino e de teste após o split.
    /// </summary>
    /// <remarks>
    /// Faixas com features IMPUTADAS ficam fora do dataset por padrão: a imputação pela mediana estratificada
    /// por gênero comprime a variância e cria uma correlação artificial feature → gênero → popularidade, e o
    /// modelo tende a aprender a mediana do gênero em vez da música. <c>includeImputed=true</c> monta o
    /// conjunto ampliado para comparação; em qualquer caso, medidas e imputadas vêm contadas separadamente.
    ///
    /// O split é determinístico por hash da faixa combinado com a semente: a mesma faixa cai sempre do mesmo
    /// lado, independentemente da ordem de leitura e do tamanho do catálogo, então duas execuções com a mesma
    /// <c>seed</c> produzem exatamente os mesmos conjuntos. Sem <c>seed</c>/<c>testFraction</c>, valem os
    /// valores de configuração — que são os usados no treino. <c>testFraction</c> é presa a [0,05; 0,50].
    ///
    /// <c>measurement</c> traz o custo real da montagem (tempo e memória), porque é ele que responde se o
    /// dataset cabe no host da demo.
    /// </remarks>
    [HttpGet("dataset/stats")]
    [ProducesResponseType(typeof(ApiResult<TrainingDatasetStatsResult>), 200)]
    public async Task<ActionResult<ApiResult<TrainingDatasetStatsResult>>> GetTrainingDatasetStats(
        [FromQuery] int? seed = null,
        [FromQuery] double? testFraction = null,
        [FromQuery] bool includeImputed = false,
        CancellationToken cancellationToken = default)
    {
        var query = new GetTrainingDatasetStatsQuery
        {
            Seed = seed,
            TestFraction = testFraction,
            IncludeImputed = includeImputed
        };

        TrainingDatasetStatsResult result = await _mediator.SendAsync(query, cancellationToken);

        return Ok(new ApiResult<TrainingDatasetStatsResult>(
            true, "Estatísticas do dataset de treino.", result));
    }

    /// <summary>
    /// Prediz a popularidade (0–100) de uma faixa. Dois modos <b>mutuamente exclusivos</b>: informe um
    /// <c>trackId</c> do catálogo <b>ou</b> um bloco de <c>features</c>, nunca os dois e nunca nenhum.
    /// </summary>
    /// <remarks>
    /// <b>Modo <c>trackId</c></b> — busca a faixa no schema <c>catalog</c>, monta o vetor de features com
    /// exatamente o mesmo pipeline do treino e prediz. Faixa inexistente → 404; faixa sem audio-features
    /// completas → 422 (não há insumo para prever). Se as features da faixa foram IMPUTADAS, a predição sai com
    /// um aviso explícito no campo <c>warnings</c> — imputado nunca passa como medido em silêncio.
    ///
    /// <b>Modo <c>features</c></b> — prediz por um bloco informado à mão, sem tocar no catálogo. As features
    /// devem respeitar o domínio do Spotify (0–1, exceto <c>tempo</c>, <c>loudness</c> e <c>durationMs</c>);
    /// valor fora de faixa → 400.
    ///
    /// A saída é presa a [0, 100] (a regressão pode extrapolar); quando isso ocorre, <c>wasClamped</c> é
    /// <c>true</c> e há um aviso. O response informa a <c>modelVersion</c> que respondeu. Sem modelo corrente
    /// publicado, o endpoint responde 404 (RFC 7807) — nunca 200 com valor default.
    ///
    /// <para><b>Exemplo — modo trackId:</b> <c>{ "trackId": "0e7ipj03S05BNilyu5bRzt" }</c></para>
    /// <para><b>Exemplo — modo features:</b>
    /// <c>{ "features": { "danceability": 0.72, "energy": 0.65, "valence": 0.5, "tempo": 120,
    /// "acousticness": 0.1, "instrumentalness": 0.0, "liveness": 0.15, "speechiness": 0.05,
    /// "loudness": -6.2, "durationMs": 210000, "explicit": false } }</c></para>
    /// </remarks>
    [HttpPost("popularity")]
    [ProducesResponseType(typeof(ApiResult<PopularityPredictionResponse>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<ApiResult<PopularityPredictionResponse>>> PredictPopularity(
        [FromBody] PopularityPredictionRequest request,
        CancellationToken cancellationToken)
    {
        PopularityPredictionResponse response =
            await _mediator.SendAsync(new PredictPopularityCommand(request), cancellationToken);

        return Ok(new ApiResult<PopularityPredictionResponse>(
            true, "Popularidade prevista.", response));
    }
}
