using System.Globalization;
using System.Runtime.CompilerServices;
using CsvHelper;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Ingestion;

/// <summary>
/// Leitor CSV (CsvHelper) do dataset Kaggle de audio-features. Lê por <b>nome de coluna do header</b> (a
/// ordem das colunas não importa), com <see cref="CultureInfo.InvariantCulture"/> para os decimais, e
/// materializa uma <see cref="KaggleAudioFeaturesRow"/> por linha — preguiçosamente.
///
/// Células vazias, ilegíveis ou colunas ausentes viram <see langword="null"/> em vez de exceção: uma linha
/// defeituosa no meio de ~114k não pode abortar a importação inteira, e o tratamento de faltantes (E1.5)
/// existe exatamente para decidir o que fazer com esses buracos — de forma explícita e marcada.
/// </summary>
internal sealed class KaggleAudioFeaturesCsvReader : IKaggleAudioFeaturesReader
{
    public async IAsyncEnumerable<KaggleAudioFeaturesRow> ReadAsync(
        string csvFilePath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(csvFilePath);
        using var reader = new StreamReader(stream);

        await foreach (KaggleAudioFeaturesRow row in ParseAsync(reader, cancellationToken))
            yield return row;
    }

    /// <summary>Parse a partir de um <see cref="TextReader"/> — testável sem tocar no disco.</summary>
    internal static async IAsyncEnumerable<KaggleAudioFeaturesRow> ParseAsync(
        TextReader textReader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var csv = new CsvReader(textReader, CultureInfo.InvariantCulture);

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new KaggleAudioFeaturesRow(
                TrackId: Text(csv, "track_id") ?? string.Empty,
                TrackName: Text(csv, "track_name"),
                Artists: Text(csv, "artists"),
                Genre: Text(csv, "track_genre"),
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
                TimeSignature: Discrete(csv, "time_signature"));
        }
    }

    private static string? Text(CsvReader csv, string column)
        => csv.TryGetField(column, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static double? Number(CsvReader csv, string column)
        => csv.TryGetField(column, out double value) ? value : null;

    private static int? Discrete(CsvReader csv, string column)
        => csv.TryGetField(column, out int value) ? value : null;
}
