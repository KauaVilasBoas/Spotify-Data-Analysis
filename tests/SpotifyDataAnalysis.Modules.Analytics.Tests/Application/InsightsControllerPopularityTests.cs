using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Modules.Analytics.Application.Controllers;
using SpotifyDataAnalysis.Modules.Analytics.Application.Insights;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Tests.Application;

/// <summary>
/// O controller de insights é fino: só monta a query e envelopa o resultado. Estes testes garantem que a
/// action de ranking traduz os query-string params (page/pageSize/genre) na
/// <see cref="GetPopularityRankingQuery"/> correta — respeitando o clamp do <see cref="PagedQuery{TResult}"/>
/// — e devolve o <see cref="ApiResult{T}"/> de sucesso com o <see cref="PagedResult{TItem}"/> do handler,
/// sem tocar em banco.
/// </summary>
public sealed class InsightsControllerPopularityTests
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

    private static PagedResult<PopularityRankingItem> SamplePage() =>
        new(
            new[]
            {
                new PopularityRankingItem("track-1", "Track One", "Artist A", 99, "pop"),
                new PopularityRankingItem("track-2", "Track Two", "Artist B", 97, null),
            },
            totalCount: 42,
            page: 2,
            pageSize: 10);

    [Fact]
    public async Task GetPopularityRanking_DispatchesQuery_AndReturnsSuccessEnvelope()
    {
        PagedResult<PopularityRankingItem> expected = SamplePage();
        var mediator = new CapturingMediator(expected);
        var controller = new InsightsController(mediator);

        ActionResult<ApiResult<PagedResult<PopularityRankingItem>>> action =
            await controller.GetPopularityRanking(page: 2, pageSize: 10, genre: "pop", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<PagedResult<PopularityRankingItem>>>(ok.Value);
        Assert.True(payload.Success);
        Assert.Same(expected, payload.Data);
    }

    [Fact]
    public async Task GetPopularityRanking_MapsQueryStringParamsIntoQuery()
    {
        var mediator = new CapturingMediator(SamplePage());
        var controller = new InsightsController(mediator);

        await controller.GetPopularityRanking(page: 3, pageSize: 25, genre: "rock", CancellationToken.None);

        var query = Assert.IsType<GetPopularityRankingQuery>(mediator.LastRequest);
        Assert.Equal(3, query.Page);
        Assert.Equal(25, query.PageSize);
        Assert.Equal("rock", query.Genre);
    }

    [Fact]
    public async Task GetPopularityRanking_ClampsPageSizeToMax_AndFloorsInvalidPage()
    {
        var mediator = new CapturingMediator(SamplePage());
        var controller = new InsightsController(mediator);

        // pageSize acima do teto (200) é limitado; page < 1 é normalizado para 1 (invariantes do PagedQuery).
        await controller.GetPopularityRanking(page: 0, pageSize: 5000, genre: null, CancellationToken.None);

        var query = Assert.IsType<GetPopularityRankingQuery>(mediator.LastRequest);
        Assert.Equal(1, query.Page);
        Assert.Equal(200, query.PageSize);
        Assert.Null(query.Genre);
    }
}
