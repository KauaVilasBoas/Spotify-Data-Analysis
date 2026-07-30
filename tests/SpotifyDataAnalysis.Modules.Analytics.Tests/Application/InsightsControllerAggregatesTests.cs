using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Modules.Analytics.Application.Controllers;
using SpotifyDataAnalysis.Modules.Analytics.Application.Insights;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Tests.Application;

/// <summary>
/// As actions de correlações e dos recortes agregados montam a query e envelopam o resultado: estes testes
/// cobrem defaults, mapeamento dos parâmetros e o clamp da paginação, sem tocar em banco.
/// </summary>
public sealed class InsightsControllerAggregatesTests
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

    private static PagedResult<T> PageOf<T>(params T[] items) => new(items, items.Length, 1, 20);

    // ---- Correlações ----

    [Fact]
    public async Task GetCorrelations_DispatchesQuery_AndReturnsSuccessEnvelope()
    {
        var expected = new FeaturePopularityCorrelationsResult(
            new[]
            {
                new FeaturePopularityCorrelation(AudioFeatureKind.Energy, -0.42, 120),
                new FeaturePopularityCorrelation(AudioFeatureKind.Tempo, null, 1),
            },
            ConsideredCount: 120,
            MeasuredCount: 120,
            ImputedCount: 30,
            IncludedImputed: false);

        var mediator = new CapturingMediator(expected);
        var controller = new InsightsController(mediator);

        ActionResult<ApiResult<FeaturePopularityCorrelationsResult>> action =
            await controller.GetCorrelations();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<FeaturePopularityCorrelationsResult>>(ok.Value);
        Assert.True(payload.Success);
        Assert.Same(expected, payload.Data);
    }

    [Fact]
    public async Task GetCorrelations_ExcludesImputedByDefault_AndHonoursTheOptIn()
    {
        var mediator = new CapturingMediator(new FeaturePopularityCorrelationsResult([], 0, 0, 0, false));
        var controller = new InsightsController(mediator);

        await controller.GetCorrelations();
        Assert.False(Assert.IsType<GetFeaturePopularityCorrelationsQuery>(mediator.LastRequest).IncludeImputed);

        await controller.GetCorrelations(includeImputed: true);
        Assert.True(Assert.IsType<GetFeaturePopularityCorrelationsQuery>(mediator.LastRequest).IncludeImputed);
    }

    // ---- Gênero ----

    [Fact]
    public async Task GetGenreInsights_MapsParamsIntoQuery_AndDefaultsToAveragePopularity()
    {
        var mediator = new CapturingMediator(
            PageOf(new GenreInsightItem("pop", 58.5, 58.5, 2, 1)));
        var controller = new InsightsController(mediator);

        await controller.GetGenreInsights();
        var byDefault = Assert.IsType<GetGenreInsightsQuery>(mediator.LastRequest);
        Assert.Equal(GenreInsightSort.AveragePopularityDesc, byDefault.Sort);
        Assert.Equal(1, byDefault.Page);
        Assert.Equal(20, byDefault.PageSize);

        await controller.GetGenreInsights(page: 2, pageSize: 50, sort: GenreInsightSort.TrackCountDesc);
        var mapped = Assert.IsType<GetGenreInsightsQuery>(mediator.LastRequest);
        Assert.Equal(2, mapped.Page);
        Assert.Equal(50, mapped.PageSize);
        Assert.Equal(GenreInsightSort.TrackCountDesc, mapped.Sort);
    }

    [Fact]
    public async Task GetGenreInsights_ClampsPageSizeToMax_AndFloorsInvalidPage()
    {
        var mediator = new CapturingMediator(PageOf<GenreInsightItem>());
        var controller = new InsightsController(mediator);

        await controller.GetGenreInsights(page: 0, pageSize: 5000);

        var query = Assert.IsType<GetGenreInsightsQuery>(mediator.LastRequest);
        Assert.Equal(1, query.Page);
        Assert.Equal(200, query.PageSize);
    }

    // ---- Artista ----

    [Fact]
    public async Task GetArtistInsights_ExcludesUnenrichedByDefault_AndHonoursTheOptIn()
    {
        var mediator = new CapturingMediator(
            PageOf(new ArtistInsightItem("art-1", "Daft Punk", 78, 8_200_000, true)));
        var controller = new InsightsController(mediator);

        await controller.GetArtistInsights();
        var byDefault = Assert.IsType<GetArtistInsightsQuery>(mediator.LastRequest);
        Assert.False(byDefault.IncludeUnenriched);
        Assert.Equal(ArtistInsightSort.PopularityDesc, byDefault.Sort);

        await controller.GetArtistInsights(includeUnenriched: true, sort: ArtistInsightSort.FollowersDesc);
        var opted = Assert.IsType<GetArtistInsightsQuery>(mediator.LastRequest);
        Assert.True(opted.IncludeUnenriched);
        Assert.Equal(ArtistInsightSort.FollowersDesc, opted.Sort);
    }

    // ---- Álbum ----

    [Fact]
    public async Task GetAlbumInsights_MapsParamsIntoQuery_AndDefaultsToAveragePopularity()
    {
        var mediator = new CapturingMediator(
            PageOf(new AlbumInsightItem("alb-1", "Homework", 1997, 78.0, 1)));
        var controller = new InsightsController(mediator);

        await controller.GetAlbumInsights();
        Assert.Equal(
            AlbumInsightSort.AveragePopularityDesc,
            Assert.IsType<GetAlbumInsightsQuery>(mediator.LastRequest).Sort);

        await controller.GetAlbumInsights(sort: AlbumInsightSort.NameAsc, page: 3);
        var mapped = Assert.IsType<GetAlbumInsightsQuery>(mediator.LastRequest);
        Assert.Equal(AlbumInsightSort.NameAsc, mapped.Sort);
        Assert.Equal(3, mapped.Page);
    }

    // ---- Ano ----

    [Fact]
    public async Task GetAlbumYearInsights_MapsParamsIntoQuery_AndDefaultsToMostRecentYearFirst()
    {
        var mediator = new CapturingMediator(
            PageOf(new AlbumYearInsightItem(2013, 82.0, 1, 1)));
        var controller = new InsightsController(mediator);

        await controller.GetAlbumYearInsights();
        Assert.Equal(
            AlbumYearInsightSort.YearDesc,
            Assert.IsType<GetAlbumYearInsightsQuery>(mediator.LastRequest).Sort);

        await controller.GetAlbumYearInsights(sort: AlbumYearInsightSort.AveragePopularityDesc, pageSize: 100);
        var mapped = Assert.IsType<GetAlbumYearInsightsQuery>(mediator.LastRequest);
        Assert.Equal(AlbumYearInsightSort.AveragePopularityDesc, mapped.Sort);
        Assert.Equal(100, mapped.PageSize);
    }

    [Fact]
    public async Task GetAlbumYearInsights_ReturnsSuccessEnvelope_WithTheNullYearRowPreserved()
    {
        // A linha de ano nulo é a das faixas sem ano conhecido; ela precisa atravessar o contrato.
        var expected = PageOf(
            new AlbumYearInsightItem(2013, 82.0, 1, 1),
            new AlbumYearInsightItem(null, 8.5, 2, 1));

        var mediator = new CapturingMediator(expected);
        var controller = new InsightsController(mediator);

        ActionResult<ApiResult<PagedResult<AlbumYearInsightItem>>> action =
            await controller.GetAlbumYearInsights();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<PagedResult<AlbumYearInsightItem>>>(ok.Value);
        Assert.Same(expected, payload.Data);
        Assert.Contains(payload.Data!.Items, item => item.Year is null);
    }
}
