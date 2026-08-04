using System.Globalization;
using System.Runtime.CompilerServices;
using CsvHelper;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

/// <summary>
/// Linha crua do CSV do Kaggle para o <b>seed do catálogo</b> (E1.10). Diferente do
/// <c>KaggleAudioFeaturesRow</c> (E1.4, que só casa features), esta traz também o que constrói a faixa:
/// <see cref="Popularity"/> (o alvo do E3), <see cref="Explicit"/> e o nome do álbum. Tudo opcional: uma
/// célula ilegível vira <see langword="null"/> e a faixa é contada como incompleta pelo seeder, nunca uma exceção.
/// </summary>
internal sealed record KaggleCatalogRow(
    string TrackId,
    string? TrackName,
    string? Artists,
    string? AlbumName,
    int? Popularity,
    int? DurationMs,
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
    string? Genre);

/// <summary>
/// Leitor CSV (CsvHelper) do dataset Kaggle para o seed do catálogo — lê por nome de coluna do header, com
/// <see cref="CultureInfo.InvariantCulture"/> para os decimais, e materializa uma <see cref="KaggleCatalogRow"/>
/// por linha de forma preguiçosa (streaming), para o pico de memória não escalar com as ~114k linhas.
/// </summary>
internal static class KaggleCatalogCsvReader
{
    public static async IAsyncEnumerable<KaggleCatalogRow> ReadAsync(
        string csvFilePath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(csvFilePath);
        using var reader = new StreamReader(stream);

        await foreach (KaggleCatalogRow row in ParseAsync(reader, cancellationToken))
            yield return row;
    }

    /// <summary>Parse a partir de um <see cref="TextReader"/> — testável sem tocar no disco.</summary>
    internal static async IAsyncEnumerable<KaggleCatalogRow> ParseAsync(
        TextReader textReader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var csv = new CsvReader(textReader, CultureInfo.InvariantCulture);

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new KaggleCatalogRow(
                TrackId: Text(csv, "track_id") ?? string.Empty,
                TrackName: Text(csv, "track_name"),
                Artists: Text(csv, "artists"),
                AlbumName: Text(csv, "album_name"),
                Popularity: Discrete(csv, "popularity"),
                DurationMs: Discrete(csv, "duration_ms"),
                Explicit: Bool(csv, "explicit"),
                Danceability: Number(csv, "danceability"),
                Energy: Number(csv, "energy"),
                Valence: Number(csv, "valence"),
                Tempo: Number(csv, "tempo"),
                Acousticness: Number(csv, "acousticness"),
                Instrumentalness: Number(csv, "instrumentalness"),
                Liveness: Number(csv, "liveness"),
                Speechiness: Number(csv, "speechiness"),
                Loudness: Number(csv, "loudness"),
                Key: Discrete(csv, "key"),
                Mode: Discrete(csv, "mode"),
                TimeSignature: Discrete(csv, "time_signature"),
                Genre: Text(csv, "track_genre"));
        }
    }

    private static string? Text(CsvReader csv, string column)
        => csv.TryGetField(column, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static double? Number(CsvReader csv, string column)
        => csv.TryGetField(column, out double value) ? value : null;

    private static int? Discrete(CsvReader csv, string column)
        => csv.TryGetField(column, out int value) ? value : null;

    private static bool Bool(CsvReader csv, string column)
        => csv.TryGetField(column, out bool value) && value;
}
