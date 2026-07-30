namespace SpotifyDataAnalysis.Modules.Analytics.Application.Insights;

/// <summary>
/// O conjunto fechado de audio-features que o read-side de EDA aceita consultar. Como o valor chega pela URL,
/// só um membro deste enum é traduzido para uma chave do jsonb por <see cref="AudioFeatureJsonKey.Of"/> —
/// nenhum nome livre alcança o SQL.
/// </summary>
public enum AudioFeatureKind
{
    Danceability,
    Energy,
    Valence,
    Tempo,
    Acousticness,
    Instrumentalness,
    Liveness,
    Speechiness,
    Loudness,
    Key,
    Mode,
    TimeSignature
}

/// <summary>
/// Traduz um <see cref="AudioFeatureKind"/> na chave correspondente do jsonb <c>audio_features</c>, cujas
/// chaves são os nomes das propriedades do value object <c>AudioFeatures</c> do Catalog (PascalCase). O
/// mapeamento é explícito para que renomear um membro do enum — parte do contrato HTTP — não mude em silêncio
/// a chave consultada no banco.
/// </summary>
public static class AudioFeatureJsonKey
{
    /// <summary>A chave do jsonb para a feature pedida.</summary>
    public static string Of(AudioFeatureKind feature) => feature switch
    {
        AudioFeatureKind.Danceability => "Danceability",
        AudioFeatureKind.Energy => "Energy",
        AudioFeatureKind.Valence => "Valence",
        AudioFeatureKind.Tempo => "Tempo",
        AudioFeatureKind.Acousticness => "Acousticness",
        AudioFeatureKind.Instrumentalness => "Instrumentalness",
        AudioFeatureKind.Liveness => "Liveness",
        AudioFeatureKind.Speechiness => "Speechiness",
        AudioFeatureKind.Loudness => "Loudness",
        AudioFeatureKind.Key => "Key",
        AudioFeatureKind.Mode => "Mode",
        AudioFeatureKind.TimeSignature => "TimeSignature",
        _ => throw new ArgumentOutOfRangeException(
            nameof(feature), feature, "Audio-feature não suportada pelo read-side de EDA.")
    };
}
