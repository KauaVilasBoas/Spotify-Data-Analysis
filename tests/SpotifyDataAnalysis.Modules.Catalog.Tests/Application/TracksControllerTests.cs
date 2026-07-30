using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Modules.Catalog.Application.Controllers;
using SpotifyDataAnalysis.Modules.Catalog.Application.Tracks;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Application;

/// <summary>
/// O <see cref="TracksController"/> traduz os parâmetros na query e envelopa o resultado: estes testes cobrem
/// essa tradução, incluindo defaults e clamp da paginação, sem tocar em banco.
/// </summary>
public sealed class TracksControllerTests
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

    private static PagedResult<TrackListItem> SamplePage() =>
        new(
            new[]
            {
                new TrackListItem("track-1", "Alpha", "Artist A", 80, 210_000, false, "album-1", true),
                new TrackListItem("track-2", "Beta", "Artist B", 55, 185_000, true, null, false),
            },
            totalCount: 2,
            page: 1,
            pageSize: 20);

    private static TrackDetailResult SampleDetail() =>
        new(
            "track-1", "Alpha", 80, 210_000, false, "BRABC1200001", "album-1",
            new[] { new TrackArtistItem("artist-1", "Artist A") },
            AudioFeatures: null);

    [Fact]
    public async Task SearchTracks_DispatchesQuery_AndReturnsSuccessEnvelope()
    {
        PagedResult<TrackListItem> expected = SamplePage();
        var mediator = new CapturingMediator(expected);
        var controller = new TracksController(mediator);

        ActionResult<ApiResult<PagedResult<TrackListItem>>> action =
            await controller.SearchTracks(search: "alpha", page: 1, pageSize: 20);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<PagedResult<TrackListItem>>>(ok.Value);
        Assert.True(payload.Success);
        Assert.Same(expected, payload.Data);
    }

    [Fact]
    public async Task SearchTracks_MapsQueryStringParamsIntoQuery()
    {
        var mediator = new CapturingMediator(SamplePage());
        var controller = new TracksController(mediator);

        await controller.SearchTracks(
            search: "daft punk", page: 3, pageSize: 25, sort: TrackSort.PopularityDesc);

        var query = Assert.IsType<SearchTracksQuery>(mediator.LastRequest);
        Assert.Equal("daft punk", query.Search);
        Assert.Equal(3, query.Page);
        Assert.Equal(25, query.PageSize);
        Assert.Equal(TrackSort.PopularityDesc, query.Sort);
    }

    [Fact]
    public async Task SearchTracks_DefaultsToNoSearch_AndAlphabeticalOrder()
    {
        var mediator = new CapturingMediator(SamplePage());
        var controller = new TracksController(mediator);

        await controller.SearchTracks();

        var query = Assert.IsType<SearchTracksQuery>(mediator.LastRequest);
        Assert.Null(query.Search);
        Assert.Equal(TrackSort.Name, query.Sort);
        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
    }

    [Fact]
    public async Task SearchTracks_ClampsPageSizeToMax_AndFloorsInvalidPage()
    {
        var mediator = new CapturingMediator(SamplePage());
        var controller = new TracksController(mediator);

        await controller.SearchTracks(page: 0, pageSize: 5000);

        var query = Assert.IsType<SearchTracksQuery>(mediator.LastRequest);
        Assert.Equal(1, query.Page);
        Assert.Equal(200, query.PageSize);
    }

    [Fact]
    public async Task GetTrackById_DispatchesQueryWithTheRouteId_AndReturnsSuccessEnvelope()
    {
        TrackDetailResult expected = SampleDetail();
        var mediator = new CapturingMediator(expected);
        var controller = new TracksController(mediator);

        ActionResult<ApiResult<TrackDetailResult>> action =
            await controller.GetTrackById("track-1", CancellationToken.None);

        var query = Assert.IsType<GetTrackByIdQuery>(mediator.LastRequest);
        Assert.Equal("track-1", query.TrackId);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<TrackDetailResult>>(ok.Value);
        Assert.True(payload.Success);
        Assert.Same(expected, payload.Data);
    }
}
