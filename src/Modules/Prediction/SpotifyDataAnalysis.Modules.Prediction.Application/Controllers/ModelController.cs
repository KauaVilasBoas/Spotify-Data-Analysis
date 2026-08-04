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
    /// <c>featureImportance</c> traz o ranking de importância do campeão (E3.6), medido por permutação sobre o
    /// conjunto de teste e gravado com a versão — inclusive <c>elapsedMilliseconds</c>, que é o custo da
    /// medição isolado do resto do treino. A leitura do ranking é o <c>GET /api/model/current</c>, que nunca
    /// recalcula; lá também está documentada a limitação da permutação diante de features correlacionadas.
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
    /// feature set, semente, tamanhos de treino/teste, as métricas do modelo <b>e</b> do baseline, e o
    /// ranking de importância das features.
    /// </summary>
    /// <remarks>
    /// O feature set e a semente vêm no contrato porque são o que torna a versão auditável: sem eles não há
    /// como reproduzir o treino nem justificar uma predição. As métricas do baseline vêm junto pelo mesmo
    /// motivo do endpoint de treino — número de modelo sozinho não permite julgar nada.
    ///
    /// **`featureImportance` (E3.6)** responde *por que* o modelo prevê o que prevê. Cada linha diz quanto a
    /// qualidade da predição se degrada quando aquela feature é embaralhada no conjunto de teste
    /// (*permutation feature importance*): `rSquaredDropMean` é a queda média de R² — o critério de ordenação —
    /// e `meanAbsoluteErrorIncreaseMean` é o preço em PONTOS DE POPULARIDADE. As duas vêm acompanhadas do
    /// desvio-padrão entre as permutações, porque a permutação é estocástica e um delta solto não distingue
    /// efeito de sorteio: quando os intervalos de duas features se sobrepõem, a ordem entre elas não significa
    /// nada. `slotCount` informa quantas colunas do vetor a linha resume — blocos categóricos one-hot, como
    /// **Genre**, aparecem agregados sob o nome do bloco, e não como uma coluna anônima por categoria.
    ///
    /// A lista é medida **uma vez, no treino**, sobre o conjunto de teste, e gravada com a versão. Esta
    /// leitura nunca recalcula: a permutação reexecuta a avaliação uma vez por coluna do vetor, o que é custo
    /// de treino e jamais de request. Versões registradas antes do E3.6 devolvem a lista **vazia** — isso é
    /// "não foi medido", não "nenhuma feature importa".
    ///
    /// **Limitação conhecida — features correlacionadas são subestimadas pelas duas.** A permutação embaralha
    /// uma coluna de cada vez; quando duas features carregam informação redundante, a que sobra intacta
    /// "cobre" a perda da que foi embaralhada, e ambas parecem menos importantes do que são. No feature set
    /// deste modelo o par mais afetado é `Energy` × `Loudness`, cuja correlação forte está medida em
    /// `GET /api/insights/correlations` (E2.4); `Acousticness` também se associa a esse par. O mesmo efeito
    /// atua DENTRO de um bloco one-hot: ao permutar o slot de um gênero, os demais slots continuam
    /// informando, então a importância agregada do bloco é um piso, não um teto. Ou seja: importância baixa
    /// aqui significa "o modelo não precisou desta coluna **dado o resto do vetor**", nunca "esta grandeza
    /// não tem relação com popularidade" — para essa segunda pergunta, o endpoint é o de correlações.
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
