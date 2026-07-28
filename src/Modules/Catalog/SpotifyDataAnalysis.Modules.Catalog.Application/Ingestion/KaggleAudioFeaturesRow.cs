namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Uma linha de audio-features do dataset Kaggle ("Spotify Tracks Dataset"), já achatada para o módulo.
/// O <see cref="TrackId"/> é o id do Spotify (chave de casamento com o catálogo coletado pela API).
/// </summary>
public sealed record KaggleAudioFeaturesRow(
    string TrackId,
    double Danceability,
    double Energy,
    double Valence,
    double Tempo,
    double Acousticness,
    double Instrumentalness,
    double Liveness,
    double Speechiness,
    double Loudness,
    int Key,
    int Mode,
    int TimeSignature);
