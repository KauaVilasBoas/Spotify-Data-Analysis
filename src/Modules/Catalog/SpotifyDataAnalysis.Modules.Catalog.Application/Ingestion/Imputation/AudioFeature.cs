namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Imputation;

/// <summary>
/// Os atributos de áudio tratáveis pela imputação. Existe para que o cálculo de medianas e o preenchimento
/// de faltantes possam iterar sobre as features de forma uniforme — sem doze blocos <c>if (x is null)</c>
/// copiados, que é onde esse tipo de código costuma apodrecer.
/// </summary>
public enum AudioFeature
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
/// Projeta uma <see cref="KaggleAudioFeaturesRow"/> como um mapa <see cref="AudioFeature"/> → valor, com
/// <see langword="null"/> onde a célula do CSV veio vazia. É a única ponte entre o formato do dataset e o
/// vocabulário da imputação: se o dataset ganhar uma coluna, só este mapeamento muda.
/// </summary>
public static class AudioFeatureAccessor
{
    /// <summary>Todas as features, na ordem da declaração — a ordem de iteração de todo o pipeline.</summary>
    public static IReadOnlyList<AudioFeature> All { get; } = Enum.GetValues<AudioFeature>();

    /// <summary>Features de valor <b>discreto</b> (índice de tonalidade, modo, fórmula de compasso): a
    /// mediana precisa ser arredondada, porque "compasso 3,5" não existe.</summary>
    public static bool IsDiscrete(AudioFeature feature)
        => feature is AudioFeature.Key or AudioFeature.Mode or AudioFeature.TimeSignature;

    public static double? ValueOf(KaggleAudioFeaturesRow row, AudioFeature feature) => feature switch
    {
        AudioFeature.Danceability => row.Danceability,
        AudioFeature.Energy => row.Energy,
        AudioFeature.Valence => row.Valence,
        AudioFeature.Tempo => row.Tempo,
        AudioFeature.Acousticness => row.Acousticness,
        AudioFeature.Instrumentalness => row.Instrumentalness,
        AudioFeature.Liveness => row.Liveness,
        AudioFeature.Speechiness => row.Speechiness,
        AudioFeature.Loudness => row.Loudness,
        AudioFeature.Key => row.Key,
        AudioFeature.Mode => row.Mode,
        AudioFeature.TimeSignature => row.TimeSignature,
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Audio feature desconhecida.")
    };
}
