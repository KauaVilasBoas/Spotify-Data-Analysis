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

    /// <summary>Ranking das faixas mais populares do catálogo, ordenado por popularidade decrescente e paginado.</summary>
    /// <remarks>
    /// Ordenação estável: <c>popularity DESC</c> com desempate determinístico por <c>id</c>, de modo que a
    /// mesma faixa nunca apareça em duas páginas. <c>pageSize</c> é limitado a 200 (clamp do <c>PagedQuery</c>).
    ///
    /// Filtro <c>genre</c> (opcional): quando ausente, o ranking cobre o catálogo INTEIRO — inclusive as faixas
    /// sem audio-features (que não têm gênero), pois popularidade não depende de features. Quando informado,
    /// restringe às faixas cujo gênero das audio-features bate com o valor, o que EXCLUI silenciosamente as
    /// faixas sem features. Cada item traz id, nome, artista principal, popularidade e o gênero (quando houver).
    /// </remarks>
    [HttpGet("popularity/top")]
    [ProducesResponseType(typeof(ApiResult<PagedResult<PopularityRankingItem>>), 200)]
    public async Task<ActionResult<ApiResult<PagedResult<PopularityRankingItem>>>> GetPopularityRanking(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? genre = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetPopularityRankingQuery { Page = page, PageSize = pageSize, Genre = genre };

        PagedResult<PopularityRankingItem> result = await _mediator.SendAsync(query, cancellationToken);

        return Ok(new ApiResult<PagedResult<PopularityRankingItem>>(
            true, "Ranking de popularidade.", result));
    }

    /// <summary>
    /// Distribuição (histograma) de uma audio-feature. Os buckets têm largura fixa entre o mínimo e o máximo
    /// observados, então o intervalo se adapta ao domínio da feature; <c>minValue</c>/<c>maxValue</c> dizem
    /// sobre qual intervalo o histograma foi desenhado e <c>buckets</c> é limitado a 200. Faixas com features
    /// imputadas ficam fora por padrão — use <c>includeImputed=true</c> para incluí-las; em qualquer caso
    /// <c>measuredCount</c>, <c>imputedCount</c> e <c>includedImputed</c> explicitam o recorte. Feature
    /// inválida responde 400 em ProblemDetails.
    /// </summary>
    [HttpGet("distributions/{feature}")]
    [ProducesResponseType(typeof(ApiResult<AudioFeatureDistributionResult>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<ActionResult<ApiResult<AudioFeatureDistributionResult>>> GetAudioFeatureDistribution(
        AudioFeatureKind feature,
        [FromQuery] int buckets = GetAudioFeatureDistributionQuery.DefaultBuckets,
        [FromQuery] bool includeImputed = false,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAudioFeatureDistributionQuery
        {
            Feature = feature,
            Buckets = buckets,
            IncludeImputed = includeImputed
        };

        AudioFeatureDistributionResult result = await _mediator.SendAsync(query, cancellationToken);

        return Ok(new ApiResult<AudioFeatureDistributionResult>(
            true, $"Distribuição de {feature}.", result));
    }

    /// <summary>
    /// Correlação de Pearson entre cada audio-feature contínua e a popularidade, calculada no banco. Cobre as 9
    /// features contínuas; <c>Key</c>, <c>Mode</c> e <c>TimeSignature</c> ficam fora porque Pearson pressupõe
    /// grandezas contínuas. Faixas com features imputadas ficam fora por padrão, porque a imputação pela mediana
    /// comprime a variância e distorce o coeficiente — <c>includeImputed=true</c> permite comparar.
    /// <c>coefficient</c> é nulo quando a amostra não permite calcular (menos de dois pares ou variância zero) e
    /// o <c>n</c> vem por feature, porque coeficiente com N pequeno é ruído.
    /// </summary>
    [HttpGet("correlations")]
    [ProducesResponseType(typeof(ApiResult<FeaturePopularityCorrelationsResult>), 200)]
    public async Task<ActionResult<ApiResult<FeaturePopularityCorrelationsResult>>> GetCorrelations(
        [FromQuery] bool includeImputed = false,
        CancellationToken cancellationToken = default)
    {
        var query = new GetFeaturePopularityCorrelationsQuery { IncludeImputed = includeImputed };

        FeaturePopularityCorrelationsResult result = await _mediator.SendAsync(query, cancellationToken);

        return Ok(new ApiResult<FeaturePopularityCorrelationsResult>(
            true, "Correlações entre audio-features e popularidade.", result));
    }
}
