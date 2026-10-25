using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Imputation;
using SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Traduz <see cref="ImputedAudioFeatures"/> para o value object <see cref="AudioFeatures"/>.
/// Fonte única da transformação — usada tanto pelo <see cref="ImportKaggleAudioFeaturesCommandHandler"/>
/// quanto pelo <see cref="ImputationDemoSeeder"/>, para que alterações na assinatura de
/// <see cref="AudioFeatures.Create"/> ou no rótulo de origem sejam refletidas em ambos os caminhos.
/// </summary>
public static class AudioFeaturesFactory
{
    /// <summary>Origem registrada nas features provenientes do dataset Kaggle.</summary>
    public const string KaggleSourceName = "kaggle:spotify-tracks-dataset";

    /// <summary>
    /// Constrói o value object a partir do resultado do imputador e do gênero da linha CSV.
    /// </summary>
    public static AudioFeatures Build(ImputedAudioFeatures values, string? genre)
        => AudioFeatures.Create(
            danceability: values[AudioFeature.Danceability],
            energy: values[AudioFeature.Energy],
            valence: values[AudioFeature.Valence],
            tempo: values[AudioFeature.Tempo],
            acousticness: values[AudioFeature.Acousticness],
            instrumentalness: values[AudioFeature.Instrumentalness],
            liveness: values[AudioFeature.Liveness],
            speechiness: values[AudioFeature.Speechiness],
            loudness: values[AudioFeature.Loudness],
            key: (int)values[AudioFeature.Key],
            mode: (int)values[AudioFeature.Mode],
            timeSignature: (int)values[AudioFeature.TimeSignature],
            source: KaggleSourceName,
            genre: genre,
            isImputed: values.IsImputed);
}
