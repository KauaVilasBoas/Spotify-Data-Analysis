using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Infrastructure.AspNetCore;
using SpotifyDataAnalysis.Modules.Prediction.Application.Models;
using SpotifyDataAnalysis.Modules.Prediction.Application.Training;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Controllers;

/// <summary>
/// Endpoints do ciclo de vida do modelo de predição (E3). Controller fino: despacha via
/// <see cref="IMediator"/> e envelopa em <see cref="ApiResult{T}"/>.
/// </summary>
[Route("api/model")]
public sealed class ModelController : SpotifyControllerBase
{
    private readonly IMediator _mediator;

    public ModelController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Treina o modelo de popularidade e devolve as métricas do run: R², MAE e RMSE do modelo e dos dois
    /// baselines (prever sempre a média do treino, e uma regressão linear sobre as mesmas features), medidos
    /// exclusivamente no conjunto de teste.
    /// </summary>
    /// <remarks>
    /// O treino avalia, sobre o MESMO split/seed, quatro feature sets (E3.3): a base do E3.2 (9 audio-features
    /// contínuas + duração + explícito), cada bloco isolado — Bloco A (Key/Mode/TimeSignature) e Bloco B
    /// (gênero, one-hot) — e a combinação. A <c>featureSetComparison</c> traz a tabela e elege o CAMPEÃO por
    /// medição: um bloco só permanece se seu ganho isolado de MAE for ≥ 2%. Só o campeão é versionado e promovido.
    ///
    /// <c>gate</c> diz se o campeão reduziu o MAE em pelo menos 5% sobre o baseline da média. É a régua de
    /// "aprendeu alguma coisa". R² é reportado mas não entra no gate: popularidade é alvo ruidoso, então R²
    /// baixo é resultado esperado, não defeito.
    ///
    /// Faixas com features imputadas ficam fora do treino por padrão. Nesse caso o resultado traz também
    /// <c>imputedComparison</c> — o MESMO modelo avaliado no conjunto de teste ampliado com as imputadas —,
    /// para o efeito da imputação aparecer medido em vez de escondido.
    ///
    /// <c>crossValidation</c> traz média e desvio-padrão entre 5 folds sobre o conjunto de treino: desvio
    /// grande significa que a métrica única do holdout é sorte, não medida.
    ///
    /// Duas execuções com a mesma <c>seed</c> produzem exatamente as mesmas métricas. O campeão é serializado,
    /// registrado como nova versão e — se passar no gate e não piorar o MAE corrente — promovido (E3.4);
    /// <c>publication</c> descreve o que aconteceu com a versão.
    /// </remarks>
    [HttpPost("train")]
    [ProducesResponseType(typeof(ApiResult<ModelTrainingReport>), 200)]
    public async Task<ActionResult<ApiResult<ModelTrainingReport>>> Train(
        [FromQuery] int? seed = null,
        [FromQuery] double? testFraction = null,
        [FromQuery] bool includeImputed = false,
        CancellationToken cancellationToken = default)
    {
        var command = new TrainPopularityModelCommand
        {
            Seed = seed,
            TestFraction = testFraction,
            IncludeImputed = includeImputed
        };

        ModelTrainingReport report = await _mediator.SendAsync(command, cancellationToken);

        return Ok(new ApiResult<ModelTrainingReport>(true, "Treino concluído.", report));
    }

    /// <summary>
    /// A ficha da versão do modelo que está respondendo as predições: número, data de treino, algoritmo,
    /// feature set, semente, tamanhos de treino/teste e as métricas do modelo <b>e</b> do baseline.
    /// </summary>
    /// <remarks>
    /// O feature set e a semente vêm no contrato porque são o que torna a versão auditável: sem eles não há
    /// como reproduzir o treino nem justificar uma predição. As métricas do baseline vêm junto pelo mesmo
    /// motivo do endpoint de treino — número de modelo sozinho não permite julgar nada.
    ///
    /// Enquanto nenhuma versão tiver sido publicada, responde **404 em ProblemDetails**, nunca 200 com corpo
    /// vazio: "ainda não treinamos" e "o modelo não sabe responder" são coisas diferentes.
    /// </remarks>
    [HttpGet("current")]
    [ProducesResponseType(typeof(ApiResult<CurrentModelResult>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<ApiResult<CurrentModelResult>>> GetCurrent(
        CancellationToken cancellationToken)
    {
        CurrentModelResult result =
            await _mediator.SendAsync(new GetCurrentModelQuery(), cancellationToken);

        return Ok(new ApiResult<CurrentModelResult>(true, "Modelo corrente.", result));
    }
}
