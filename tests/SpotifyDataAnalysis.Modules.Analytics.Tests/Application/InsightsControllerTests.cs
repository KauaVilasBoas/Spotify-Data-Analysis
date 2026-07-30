using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Modules.Analytics.Application.Controllers;
using SpotifyDataAnalysis.Modules.Analytics.Application.Insights;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Tests.Application;

/// <summary>
/// O controller de insights é fino: só despacha a query e envelopa o resultado. O teste garante que ele
/// monta a <see cref="GetCatalogSummaryQuery"/> e devolve o <see cref="ApiResult{T}"/> de sucesso com o
/// payload do handler — sem tocar em banco.
/// </summary>
public sealed class InsightsControllerTests
{
    private sealed class CapturingMediator : IMediator
    {
        private readonly object _result;

        public object? LastRequest { get; private set; }

        public CapturingMediator(object result) => _result = result;

        public Task<TResult> SendAsync<TResult>(
            IRequest<TResult> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResult)_result);
        }
    }

    [Fact]
    public async Task GetSummary_DispatchesQuery_AndReturnsSuccessEnvelope()
    {
        var expected = new CatalogSummaryResult(
            TotalTracks: 100,
            TracksWithAudioFeatures: 80,
            TracksWithoutAudioFeatures: 20,
            TracksWithMeasuredFeatures: 65,
            TracksWithImputedFeatures: 15,
            DistinctArtists: 42,
            DistinctAlbums: 30,
            DistinctGenres: 12);
        var mediator = new CapturingMediator(expected);
        var controller = new InsightsController(mediator);

        ActionResult<ApiResult<CatalogSummaryResult>> action =
            await controller.GetSummary(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<CatalogSummaryResult>>(ok.Value);
        Assert.True(payload.Success);
        Assert.Same(expected, payload.Data);

        Assert.IsType<GetCatalogSummaryQuery>(mediator.LastRequest);
    }
}
