using SpotifyDataAnalysis.Modules.Prediction.Contracts.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;
using SpotifyDataAnalysis.SharedKernel.Exceptions;
using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.Modules.Prediction.Application.Inference;

/// <summary>
/// Prever a popularidade de uma faixa (E3.5). É <b>command</b>, e não query, por dois motivos: não é uma
/// leitura pura (invoca um serviço externo de inferência que mantém pool de engines com estado) e o vocabulário
/// do domínio é "prever", um ato. Não muda estado persistente, então não abre transação.
///
/// <para>Carrega o request público cru (<see cref="PopularityPredictionRequest"/>). A tradução para os value
/// objects do domínio — e a aplicação da invariante XOR — acontece no handler, depois que o
/// <see cref="PredictPopularityCommandValidator"/> já garantiu, na fronteira HTTP, que a forma da mensagem é
/// válida (400 quando não é).</para>
/// </summary>
public sealed record PredictPopularityCommand(PopularityPredictionRequest Request)
    : ICommand<PopularityPredictionResponse>;

internal sealed class PredictPopularityCommandHandler
    : ICommandHandler<PredictPopularityCommand, PopularityPredictionResponse>
{
    private const string ImputedFeaturesWarning =
        "As features desta faixa foram IMPUTADAS (preenchidas por estimativa), não medidas — a predição usa " +
        "um insumo aproximado e deve ser lida com essa ressalva.";

    private const string ClampedOutputWarning =
        "O modelo emitiu um score fora do intervalo [0, 100] e a saída foi limitada ao domínio da " +
        "popularidade — sinal de que a faixa está no extremo do que o modelo aprendeu.";

    private readonly ITrackFeatureSource _trackFeatureSource;
    private readonly IPopularityPredictor _predictor;

    public PredictPopularityCommandHandler(
        ITrackFeatureSource trackFeatureSource, IPopularityPredictor predictor)
    {
        _trackFeatureSource = trackFeatureSource;
        _predictor = predictor;
    }

    public async Task<PopularityPredictionResponse> HandleAsync(
        PredictPopularityCommand request, CancellationToken cancellationToken = default)
    {
        // A invariante XOR já foi checada pelo validador (400 quando ambígua). Aqui ela é reafirmada pelo tipo
        // de domínio, que é o único capaz de construir um insumo válido — defesa em profundidade, não repetição.
        PopularityPredictionInput input = ToDomainInput(request.Request);

        return input.Mode == PopularityPredictionMode.ByTrackId
            ? await PredictByTrackIdAsync(input.TrackId!, cancellationToken)
            : await PredictByFeaturesAsync(input.Features!, imputed: false, input.Mode, cancellationToken);
    }

    private async Task<PopularityPredictionResponse> PredictByTrackIdAsync(
        string trackId, CancellationToken cancellationToken)
    {
        TrackFeatureRow? row = await _trackFeatureSource.FindByTrackIdAsync(trackId, cancellationToken);

        // Faixa inexistente e faixa sem insumo são erros diferentes, e o cliente precisa distingui-los: um é
        // "esse id não existe" (404), o outro é "existe, mas não dá para prever" (regra de negócio, 422).
        if (row is null)
            throw new NotFoundException("Track", trackId);

        if (!row.HasAudioFeatures || !row.HasCompleteFeatures)
            throw new BusinessException(
                $"A faixa '{trackId}' não tem audio-features completas no catálogo — sem esse insumo não há " +
                "como prever a popularidade. Use o modo de features informadas se quiser prever mesmo assim.");

        AudioFeatureInput features = AudioFeatureInput.Create(
            row.Danceability!.Value,
            row.Energy!.Value,
            row.Valence!.Value,
            row.Tempo!.Value,
            row.Acousticness!.Value,
            row.Instrumentalness!.Value,
            row.Liveness!.Value,
            row.Speechiness!.Value,
            row.Loudness!.Value,
            row.DurationMs,
            row.Explicit);

        return await PredictByFeaturesAsync(
            features, row.IsImputed, PopularityPredictionMode.ByTrackId, cancellationToken);
    }

    private async Task<PopularityPredictionResponse> PredictByFeaturesAsync(
        AudioFeatureInput features,
        bool imputed,
        PopularityPredictionMode mode,
        CancellationToken cancellationToken)
    {
        PopularityPrediction prediction = await _predictor.PredictAsync(features, cancellationToken);

        return BuildResponse(prediction, imputed, mode);
    }

    private static PopularityPredictionResponse BuildResponse(
        PopularityPrediction prediction, bool imputed, PopularityPredictionMode mode)
    {
        var warnings = new List<string>();

        if (imputed)
            warnings.Add(ImputedFeaturesWarning);

        if (prediction.Popularity.WasClamped)
            warnings.Add(ClampedOutputWarning);

        return new PopularityPredictionResponse
        {
            PredictedPopularity = prediction.Popularity.Value,
            RawScore = prediction.Popularity.RawScore,
            WasClamped = prediction.Popularity.WasClamped,
            ModelVersion = prediction.ModelVersion,
            Mode = DescribeMode(mode),
            Warnings = warnings
        };
    }

    private static string DescribeMode(PopularityPredictionMode mode) =>
        mode == PopularityPredictionMode.ByTrackId ? "trackId" : "features";

    private static PopularityPredictionInput ToDomainInput(PopularityPredictionRequest request)
    {
        AudioFeatureInput? features = request.Features is null
            ? null
            : AudioFeatureInput.Create(
                request.Features.Danceability,
                request.Features.Energy,
                request.Features.Valence,
                request.Features.Tempo,
                request.Features.Acousticness,
                request.Features.Instrumentalness,
                request.Features.Liveness,
                request.Features.Speechiness,
                request.Features.Loudness,
                request.Features.DurationMs,
                request.Features.Explicit);

        return PopularityPredictionInput.FromRequest(request.TrackId, features);
    }
}
