namespace SpotifyDataAnalysis.Modules.Prediction.Application.Inference;

/// <summary>
/// Leitura pontual das features de UMA faixa do catálogo, para o modo <c>trackId</c> da predição (E3.5). É a
/// mesma fronteira do E3.1: o Prediction lê o schema <c>catalog</c> por SQL, sem referenciar tipo algum do
/// Catalog (acoplamento por DADO, guardado pelos ArchTests).
/// </summary>
public interface ITrackFeatureSource
{
    /// <summary>
    /// Busca as features da faixa pelo id. Devolve <c>null</c> quando a faixa não existe no catálogo — a
    /// distinção entre "não existe" (404) e "existe mas sem features" (erro de insumo) é decidida pelo estado
    /// dos campos, não engolida aqui.
    /// </summary>
    Task<TrackFeatureRow?> FindByTrackIdAsync(string trackId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A projeção crua das features de uma faixa do catálogo. Todos os campos de áudio são anuláveis porque uma
/// faixa pode existir sem <c>audio_features</c> (jsonb nulo) ou com o jsonb incompleto — o handler decide o que
/// isso significa. <see cref="IsImputed"/> viaja para que a predição possa sinalizar quando o insumo não foi
/// medido, e não maquiar a imputação como medida.
/// </summary>
public sealed record TrackFeatureRow(
    string TrackId,
    bool HasAudioFeatures,
    bool IsImputed,
    int DurationMs,
    bool Explicit,
    double? Danceability,
    double? Energy,
    double? Valence,
    double? Tempo,
    double? Acousticness,
    double? Instrumentalness,
    double? Liveness,
    double? Speechiness,
    double? Loudness,
    int? Key,
    int? Mode,
    int? TimeSignature,
    string? Genre)
{
    /// <summary>
    /// Se as nove grandezas contínuas exigidas pelo feature set corrente estão presentes. Uma faixa com jsonb
    /// não nulo mas com alguma chave faltando não tem insumo para prever, tanto quanto uma sem features.
    /// </summary>
    public bool HasCompleteFeatures =>
        Danceability.HasValue
        && Energy.HasValue
        && Valence.HasValue
        && Tempo.HasValue
        && Acousticness.HasValue
        && Instrumentalness.HasValue
        && Liveness.HasValue
        && Speechiness.HasValue
        && Loudness.HasValue;
}
