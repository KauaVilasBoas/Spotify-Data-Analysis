using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Infrastructure.AspNetCore;
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
    /// O feature set desta fatia são as 9 audio-features contínuas mais duração e explícito. Gênero, artista e
    /// <c>Key</c>/<c>Mode</c>/<c>TimeSignature</c> ficam de fora por decisão de escopo — entram no E3.3, e o
    /// ganho deles é medido contra os números deste endpoint.
    ///
    /// <c>gate</c> diz se o modelo reduziu o MAE em pelo menos 5% sobre o baseline da média. É a régua de
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
    /// Duas execuções com a mesma <c>seed</c> produzem exatamente as mesmas métricas. O artefato treinado
    /// **não** é persistido aqui — versionamento é o E3.4.
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
}
