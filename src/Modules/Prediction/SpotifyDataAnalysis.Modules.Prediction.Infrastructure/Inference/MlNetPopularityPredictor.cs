using Microsoft.ML;
using SpotifyDataAnalysis.Modules.Prediction.Application.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;
using SpotifyDataAnalysis.Modules.Prediction.Domain.Models;
using SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Training;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Infrastructure.Inference;

/// <summary>
/// Implementação da porta de inferência sobre o ML.NET. É o único ponto do módulo onde um score é produzido, e
/// mora na Infrastructure porque conhece o framework — a Application enxerga só <see cref="IPopularityPredictor"/>.
///
/// <para>O caminho é: resolver a versão corrente do banco (fonte da verdade, E3.4) → garantir o
/// <see cref="ITransformer"/> desserializado no <c>CurrentModelCache</c> (custo de carga pago uma vez) → alugar
/// um engine do <see cref="PopularityPredictionEnginePool"/> para aquela versão → montar a linha de features
/// com o MESMO <c>PopularityTrainingRow.FromAudioFeatures</c> do treino → clampar a saída no domínio [0, 100].</para>
///
/// <para>Scoped, e não singleton: depende do <see cref="IModelVersionRepository"/> (EF, per-request). O estado
/// que precisa sobreviver entre requisições — o modelo desserializado e os engines — vive no cache e no pool,
/// ambos singletons; este predictor é uma orquestração fina e sem estado.</para>
/// </summary>
internal sealed class MlNetPopularityPredictor : IPopularityPredictor
{
    private readonly IModelVersionRepository _versionRepository;
    private readonly CurrentModelCache _modelCache;
    private readonly PopularityPredictionEnginePool _enginePool;

    public MlNetPopularityPredictor(
        IModelVersionRepository versionRepository,
        CurrentModelCache modelCache,
        PopularityPredictionEnginePool enginePool)
    {
        _versionRepository = versionRepository;
        _modelCache = modelCache;
        _enginePool = enginePool;
    }

    /// <inheritdoc />
    public async Task<PopularityPrediction> PredictAsync(
        AudioFeatureInput features, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(features);

        ModelVersion current = await _versionRepository.GetCurrentAsync(cancellationToken)
            ?? throw new NotFoundException(
                "Nenhum modelo de popularidade foi publicado ainda — não há como prever. Treine e promova um " +
                "modelo em POST /api/model/train.");

        // O cache desserializa o .zip do bytea só quando frio ou apontando para outra versão. O pool cria/reusa
        // engines a partir DESSE transformer, então o custo de carga é pago uma vez por versão, não por request.
        ITransformer model = await _modelCache.GetOrLoadAsync(current, cancellationToken);

        PopularityTrainingRow row = PopularityTrainingRow.FromAudioFeatures(
            features.Danceability,
            features.Energy,
            features.Valence,
            features.Tempo,
            features.Acousticness,
            features.Instrumentalness,
            features.Liveness,
            features.Speechiness,
            features.Loudness,
            features.DurationMs,
            features.Explicit,
            features.Key,
            features.Mode,
            features.TimeSignature,
            features.Genre);

        float score = Predict(current.Id, model, row);

        return new PopularityPrediction(PredictedPopularity.FromRawScore(score), current.Id);
    }

    /// <summary>
    /// A operação sob concorrência: aluga um engine do pool (thread-safe), usa-o numa única thread pela vida da
    /// concessão e o devolve. O <c>using</c> garante o retorno mesmo se a predição lançar.
    /// </summary>
    private float Predict(int version, ITransformer model, PopularityTrainingRow row)
    {
        using PopularityPredictionEnginePool.EngineLease lease = _enginePool.Rent(version, model);

        return lease.Engine.Predict(row).Score;
    }
}
