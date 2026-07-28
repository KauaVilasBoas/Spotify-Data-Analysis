using System.Globalization;
using System.Runtime.CompilerServices;
using CsvHelper;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Ingestion;

/// <summary>
/// Leitor CSV (CsvHelper) do dataset Kaggle de audio-features. Lê por <b>nome de coluna do header</b> (a
/// ordem das colunas não importa), com <see cref="CultureInfo.InvariantCulture"/> para os decimais, e
/// materializa uma <see cref="KaggleAudioFeaturesRow"/> por linha — preguiçosamente.
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
                TrackId: csv.GetField<string>("track_id") ?? string.Empty,
                TrackName: csv.GetField<string>("track_name"),
                Artists: csv.GetField<string>("artists"),
                Genre: csv.GetField<string>("track_genre"),
                Danceability: csv.GetField<double>("danceability"),
                Energy: csv.GetField<double>("energy"),
                Valence: csv.GetField<double>("valence"),
                Tempo: csv.GetField<double>("tempo"),
                Acousticness: csv.GetField<double>("acousticness"),
                Instrumentalness: csv.GetField<double>("instrumentalness"),
                Liveness: csv.GetField<double>("liveness"),
                Speechiness: csv.GetField<double>("speechiness"),
                Loudness: csv.GetField<double>("loudness"),
                Key: csv.GetField<int>("key"),
                Mode: csv.GetField<int>("mode"),
                TimeSignature: csv.GetField<int>("time_signature"));
        }
    }
}
