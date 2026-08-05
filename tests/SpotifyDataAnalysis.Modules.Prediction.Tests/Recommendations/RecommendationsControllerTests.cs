using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Modules.Prediction.Application.Controllers;
using SpotifyDataAnalysis.Modules.Prediction.Application.Recommendations;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Recommendations;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Recommendations;

/// <summary>
/// O controller público é fino: monta a <see cref="GetTrackRecommendationsQuery"/> com id/limit/explainTopK,
/// despacha via mediator e envelopa em <see cref="ApiResult{T}"/> de sucesso. Erros sobem para o middleware
/// (RFC 7807) — o controller não os mapeia.
/// </summary>
public sealed class RecommendationsControllerTests
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
    public async Task GetTrackRecommendations_DispatchesQueryWithParams_AndReturnsSuccessEnvelope()
    {
        var expected = new TrackRecommendationsResponse
        {
            SeedTrackId = "0e7ipj03S05BNilyu5bRzt",
            SeedName = "Seed",
            IndexedTrackCount = 89_712,
            Recommendations = [],
            Warnings = []
        };
        var mediator = new CapturingMediator(expected);
        var controller = new RecommendationsController(mediator);

        ActionResult<ApiResult<TrackRecommendationsResponse>> action =
            await controller.GetTrackRecommendations("0e7ipj03S05BNilyu5bRzt", limit: 5, explainTopK: 2,
                genreMode: GenreRankingModeContract.SameGenreOnly, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<TrackRecommendationsResponse>>(ok.Value);
        Assert.True(payload.Success);
        Assert.Same(expected, payload.Data);

        var query = Assert.IsType<GetTrackRecommendationsQuery>(mediator.LastRequest);
        Assert.Equal("0e7ipj03S05BNilyu5bRzt", query.SeedTrackId);
        Assert.Equal(5, query.Limit);
        Assert.Equal(2, query.ExplainTopK);
        Assert.Equal(GenreRankingModeContract.SameGenreOnly, query.GenreMode);
    }

    [Fact]
    public async Task GetTrackRecommendations_UsesDefaults_WhenQueryParamsOmitted()
    {
        var mediator = new CapturingMediator(new TrackRecommendationsResponse());
        var controller = new RecommendationsController(mediator);

        await controller.GetTrackRecommendations("seed", cancellationToken: CancellationToken.None);

        var query = Assert.IsType<GetTrackRecommendationsQuery>(mediator.LastRequest);
        Assert.Equal(GetTrackRecommendationsQuery.DefaultLimit, query.Limit);
        Assert.Equal(GetTrackRecommendationsQuery.DefaultExplainTopK, query.ExplainTopK);
        Assert.Equal(GetTrackRecommendationsQuery.DefaultGenreMode, query.GenreMode);
        Assert.Equal(GenreRankingModeContract.Boost, query.GenreMode);
    }
}
