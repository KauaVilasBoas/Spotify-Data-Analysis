using SpotifyDataAnalysis.Modules.Prediction.Application.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Contracts.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Application;

/// <summary>
/// A orquestração do caso de uso de predição (E3.5): resolução do modo, distinção 404/422 no modo trackId,
/// aviso de imputação nunca silencioso, e propagação de versão/clamp para o response. Usa fakes das portas —
/// nem catálogo nem ML.NET entram aqui.
/// </summary>
public sealed class PredictPopularityCommandHandlerTests
{
    private sealed class StubPredictor : IPopularityPredictor
    {
        private readonly PopularityPrediction _result;

        public StubPredictor(double rawScore, int modelVersion) =>
            _result = new PopularityPrediction(PredictedPopularity.FromRawScore(rawScore), modelVersion);

        public AudioFeatureInput? LastFeatures { get; private set; }

        public Task<PopularityPrediction> PredictAsync(
            AudioFeatureInput features, CancellationToken cancellationToken = default)
        {
            LastFeatures = features;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubTrackFeatureSource : ITrackFeatureSource
    {
        private readonly TrackFeatureRow? _row;

        public StubTrackFeatureSource(TrackFeatureRow? row) => _row = row;

        public Task<TrackFeatureRow?> FindByTrackIdAsync(
            string trackId, CancellationToken cancellationToken = default) => Task.FromResult(_row);
    }

    private static TrackFeatureRow CompleteRow(string trackId = "t1", bool imputed = false) => new(
        TrackId: trackId,
        HasAudioFeatures: true,
        IsImputed: imputed,
        DurationMs: 200_000,
        Explicit: false,
        Danceability: 0.5,
        Energy: 0.6,
        Valence: 0.4,
        Tempo: 120.0,
        Acousticness: 0.1,
        Instrumentalness: 0.0,
        Liveness: 0.2,
        Speechiness: 0.05,
        Loudness: -6.0);

    private static AudioFeaturesPayload ValidPayload() => new()
    {
        Danceability = 0.7,
        Energy = 0.65,
        Valence = 0.5,
        Tempo = 120.0,
        Acousticness = 0.1,
        Instrumentalness = 0.0,
        Liveness = 0.15,
        Speechiness = 0.05,
        Loudness = -6.2,
        DurationMs = 210_000,
        Explicit = false
    };

    [Fact]
    public async Task ByTrackId_WithCompleteFeatures_PredictsAndReportsVersionAndMode()
    {
        var predictor = new StubPredictor(rawScore: 57.3, modelVersion: 4);
        var handler = new PredictPopularityCommandHandler(
            new StubTrackFeatureSource(CompleteRow()), predictor);

        PopularityPredictionResponse response = await handler.HandleAsync(
            new PredictPopularityCommand(new PopularityPredictionRequest { TrackId = "t1" }));

        Assert.Equal(57.3, response.PredictedPopularity);
        Assert.Equal(4, response.ModelVersion);
        Assert.Equal("trackId", response.Mode);
        Assert.Empty(response.Warnings);
        Assert.NotNull(predictor.LastFeatures);
    }

    [Fact]
    public async Task ByTrackId_WithImputedFeatures_PredictsWithExplicitWarning()
    {
        var handler = new PredictPopularityCommandHandler(
            new StubTrackFeatureSource(CompleteRow(imputed: true)),
            new StubPredictor(rawScore: 40.0, modelVersion: 2));

        PopularityPredictionResponse response = await handler.HandleAsync(
            new PredictPopularityCommand(new PopularityPredictionRequest { TrackId = "t1" }));

        Assert.Single(response.Warnings);
        Assert.Contains("IMPUTADAS", response.Warnings[0]);
    }

    [Fact]
    public async Task ByTrackId_WhenTrackDoesNotExist_ThrowsNotFound()
    {
        var handler = new PredictPopularityCommandHandler(
            new StubTrackFeatureSource(row: null),
            new StubPredictor(rawScore: 1, modelVersion: 1));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(
            new PredictPopularityCommand(new PopularityPredictionRequest { TrackId = "ghost" })));
    }

    [Fact]
    public async Task ByTrackId_WhenTrackHasNoAudioFeatures_ThrowsBusiness()
    {
        TrackFeatureRow noFeatures = CompleteRow() with
        {
            HasAudioFeatures = false,
            Danceability = null
        };

        var handler = new PredictPopularityCommandHandler(
            new StubTrackFeatureSource(noFeatures),
            new StubPredictor(rawScore: 1, modelVersion: 1));

        await Assert.ThrowsAsync<BusinessException>(() => handler.HandleAsync(
            new PredictPopularityCommand(new PopularityPredictionRequest { TrackId = "t1" })));
    }

    [Fact]
    public async Task ByFeatures_DoesNotConsultCatalog_AndReportsFeaturesMode()
    {
        // A fonte devolveria null (faixa inexistente) se fosse consultada — como o modo features não toca no
        // catálogo, a predição sai mesmo assim, provando que o caminho não passou pela leitura por trackId.
        var unusedSource = new StubTrackFeatureSource(row: null);
        var handler = new PredictPopularityCommandHandler(
            unusedSource, new StubPredictor(rawScore: 62.0, modelVersion: 5));

        PopularityPredictionResponse response = await handler.HandleAsync(
            new PredictPopularityCommand(new PopularityPredictionRequest { Features = ValidPayload() }));

        Assert.Equal(62.0, response.PredictedPopularity);
        Assert.Equal("features", response.Mode);
    }

    [Fact]
    public async Task WhenModelExtrapolates_ResponseCarriesClampWarning()
    {
        var handler = new PredictPopularityCommandHandler(
            new StubTrackFeatureSource(CompleteRow()),
            new StubPredictor(rawScore: 118.0, modelVersion: 3));

        PopularityPredictionResponse response = await handler.HandleAsync(
            new PredictPopularityCommand(new PopularityPredictionRequest { TrackId = "t1" }));

        Assert.Equal(100.0, response.PredictedPopularity);
        Assert.True(response.WasClamped);
        Assert.Contains(response.Warnings, warning => warning.Contains("fora do intervalo"));
    }
}
