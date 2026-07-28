namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Uma linha de audio-features do dataset Kaggle ("Spotify Tracks Dataset"), já achatada para o módulo.
///
/// Carrega as <b>duas</b> chaves de casamento com o catálogo: o <see cref="TrackId"/> (id do Spotify, o
/// caminho preferencial) e o par <see cref="TrackName"/> + <see cref="Artists"/>, usado no fallback quando o
/// id não bate. <see cref="Artists"/> vem como a API do dataset entrega — nomes separados por <c>;</c>.
/// </summary>
public sealed record KaggleAudioFeaturesRow(
    string TrackId,
    string? TrackName,
    string? Artists,
    string? Genre,
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
