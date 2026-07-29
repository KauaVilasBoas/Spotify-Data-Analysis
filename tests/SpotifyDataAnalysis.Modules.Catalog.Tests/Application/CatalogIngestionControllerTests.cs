using Microsoft.AspNetCore.Mvc;
using SpotifyDataAnalysis.Modules.Catalog.Application.Controllers;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.SharedKernel.Http;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Application;

/// <summary>
/// O controller de ingestão é fino: só despacha o command e envelopa o resultado. O teste garante que
/// ele monta o <see cref="IngestPlaylistCommand"/> certo e devolve o <see cref="ApiResult{T}"/> de sucesso.
/// </summary>
public sealed class CatalogIngestionControllerTests
{
    private sealed class CapturingMediator : IMediator
    {
        private readonly object _result;

        public object? LastRequest { get; private set; }

        public CapturingMediator(object result) => _result = result;

        public Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResult)_result);
        }
    }

    [Fact]
    public async Task IngestPlaylist_DispatchesCommand_AndReturnsSuccessEnvelope()
    {
        var expected = new IngestPlaylistResult(Ingested: 2, Updated: 1, Skipped: 0, Duplicates: 0, Total: 3);
        var mediator = new CapturingMediator(expected);
        var controller = new CatalogIngestionController(mediator);

        ActionResult<ApiResult<IngestPlaylistResult>> action =
            await controller.IngestPlaylist("pl1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var payload = Assert.IsType<ApiResult<IngestPlaylistResult>>(ok.Value);
        Assert.True(payload.Success);
        Assert.Same(expected, payload.Data);

        var command = Assert.IsType<IngestPlaylistCommand>(mediator.LastRequest);
        Assert.Equal("pl1", command.SpotifyPlaylistId);
    }
}
