using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Modules.Analytics.Application.Controllers;
using SpotifyDataAnalysis.Modules.Analytics.Application.Insights;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Analytics.Tests.Application;

/// <summary>
/// A action de distribuição monta a query e envelopa o resultado: estes testes cobrem o mapeamento dos
/// parâmetros e o teto do número de buckets, sem tocar em banco.
/// </summary>
public sealed class InsightsControllerDistributionTests
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

    private static AudioFeatureDistributionResult SampleDistribution() =>
        new(
            AudioFeatureKind.Danceability,
            new[]
            {
                new AudioFeatureDistributionBucket(1, 0.10, 0.55, 12),
                new AudioFeatureDistributionBucket(2, 0.55, 1.00, 30),
            },
            TotalConsidered: 42,
            MeasuredCount: 42,
            ImputedCount: 7,
            IncludedImputed: false,
            MinValue: 0.10,
            MaxValue: 1.00);

    [Fact]
    public async Task GetAudioFeatureDistribution_DispatchesQuery_AndReturnsSuccessEnvelope()
    {
        AudioFeatureDistributionResult expected = SampleDistribution();
        var mediator = new CapturingMediator(expected);
        var controller = new InsightsController(mediator);

        ActionResult<ApiResult<AudioFeatureDistributionResult>> action =
            await controller.GetAudioFeatureDistribution(
                AudioFeatureKind.Danceability, buckets: 20, includeImputed: false, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<AudioFeatureDistributionResult>>(ok.Value);
        Assert.True(payload.Success);
        Assert.Same(expected, payload.Data);
    }

    [Fact]
    public async Task GetAudioFeatureDistribution_MapsRouteAndQueryStringParamsIntoQuery()
    {
        var mediator = new CapturingMediator(SampleDistribution());
        var controller = new InsightsController(mediator);

        await controller.GetAudioFeatureDistribution(
            AudioFeatureKind.Loudness, buckets: 35, includeImputed: true, CancellationToken.None);

        var query = Assert.IsType<GetAudioFeatureDistributionQuery>(mediator.LastRequest);
        Assert.Equal(AudioFeatureKind.Loudness, query.Feature);
        Assert.Equal(35, query.Buckets);
        Assert.True(query.IncludeImputed);
    }

    [Fact]
    public async Task GetAudioFeatureDistribution_ExcludesImputedByDefault()
    {
        var mediator = new CapturingMediator(SampleDistribution());
        var controller = new InsightsController(mediator);

        await controller.GetAudioFeatureDistribution(AudioFeatureKind.Energy);

        var query = Assert.IsType<GetAudioFeatureDistributionQuery>(mediator.LastRequest);
        Assert.False(query.IncludeImputed);
        Assert.Equal(GetAudioFeatureDistributionQuery.DefaultBuckets, query.Buckets);
    }

    [Theory]
    [InlineData(5000, GetAudioFeatureDistributionQuery.MaxBuckets)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    public async Task GetAudioFeatureDistribution_ClampsBucketCount(int requested, int expected)
    {
        var mediator = new CapturingMediator(SampleDistribution());
        var controller = new InsightsController(mediator);

        await controller.GetAudioFeatureDistribution(AudioFeatureKind.Valence, buckets: requested);

        var query = Assert.IsType<GetAudioFeatureDistributionQuery>(mediator.LastRequest);
        Assert.Equal(expected, query.Buckets);
    }
}
