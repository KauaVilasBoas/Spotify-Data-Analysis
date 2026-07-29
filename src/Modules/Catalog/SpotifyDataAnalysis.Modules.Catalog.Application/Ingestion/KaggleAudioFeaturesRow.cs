namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

/// <summary>
/// Uma linha de audio-features do dataset Kaggle ("Spotify Tracks Dataset"), já achatada para o módulo.
///
/// Carrega as chaves de casamento com o catálogo: o <see cref="TrackId"/> (id do Spotify, o caminho
/// preferencial), o par <see cref="TrackName"/> + <see cref="Artists"/> (o fallback textual quando o id não
/// bate) e a <see cref="DurationMs"/>, que desambigua homônimos do mesmo artista — faixas distintas que a
/// chave textual confunde (E1.9). <see cref="Artists"/> vem como o dataset entrega — nomes separados por
/// <c>;</c>.
///
/// Os atributos numéricos são <b>anuláveis de propósito</b>: uma célula vazia é ausência de medição, não
/// zero. Confundir as duas coisas envenenaria as estatísticas da EDA (E2) e o treino do modelo (E3) — o
/// preenchimento é decidido explicitamente pela imputação (E1.5), que marca o que inferiu.
///
/// A <see cref="DurationMs"/> também é anulável: sem duração no CSV, a estratégia de casamento por duração
/// simplesmente cede a vez, sem inventar um valor.
/// </summary>
public sealed record KaggleAudioFeaturesRow(
    string TrackId,
    string? TrackName,
    string? Artists,
    string? Genre,
    int? DurationMs,
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
    int? TimeSignature);
