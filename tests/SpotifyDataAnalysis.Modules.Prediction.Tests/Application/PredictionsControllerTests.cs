using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Modules.Prediction.Application.Controllers;
using SpotifyDataAnalysis.Modules.Prediction.Application.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Inference;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Application;

/// <summary>
/// O controller de predição é fino: monta o <see cref="PredictPopularityCommand"/> com o request recebido,
/// despacha via mediator e envelopa o resultado em <see cref="ApiResult{T}"/> de sucesso. Erros sobem para o
/// middleware (RFC 7807) — o controller não os mapeia.
/// </summary>
public sealed class PredictionsControllerTests
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
    public async Task PredictPopularity_DispatchesCommandWithRequest_AndReturnsSuccessEnvelope()
    {
        var expected = new PopularityPredictionResponse
        {
            PredictedPopularity = 58.4,
            RawScore = 58.4,
            WasClamped = false,
            ModelVersion = 7,
            Mode = "trackId",
            Warnings = []
        };
        var mediator = new CapturingMediator(expected);
        var controller = new PredictionsController(mediator);
        var request = new PopularityPredictionRequest { TrackId = "0e7ipj03S05BNilyu5bRzt" };

        ActionResult<ApiResult<PopularityPredictionResponse>> action =
            await controller.PredictPopularity(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<PopularityPredictionResponse>>(ok.Value);
        Assert.True(payload.Success);
        Assert.Same(expected, payload.Data);

        var command = Assert.IsType<PredictPopularityCommand>(mediator.LastRequest);
        Assert.Same(request, command.Request);
    }
}
