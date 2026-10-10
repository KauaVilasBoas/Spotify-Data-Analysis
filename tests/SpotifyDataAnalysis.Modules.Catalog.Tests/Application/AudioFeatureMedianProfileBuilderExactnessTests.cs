using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion;
using SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Imputation;
using SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Ingestion;

namespace SpotifyDataAnalysis.Modules.Catalog.Tests.Application;

/// <summary>
/// Trava a propriedade que justifica o <see cref="AudioFeatureMedianProfileBuilder"/> existir: as medianas
/// (global e por gênero) são <b>exatamente</b> as de ordenar todas as observações, bit a bit. Qualquer
/// otimização de memória do builder tem de passar por aqui sem tolerância.
/// </summary>
public sealed class AudioFeatureMedianProfileBuilderExactnessTests
{
    private static readonly string?[] Genres = ["rock", "Pop ", "pop", "jazz", "solo", "", "  ", null];

    private static List<KaggleAudioFeaturesRow> DeterministicRows()
    {
        var random = new Random(20240607);
        double? Maybe(double value) => random.Next(5) == 0 ? null : Math.Round(value, 2);

        var rows = new List<KaggleAudioFeaturesRow>();
        for (int i = 0; i < 1_201; i++)
        {
            string? genre = i == 0 ? "solo" : Genres[1 + random.Next(Genres.Length - 1)];
            if (genre == "solo" && i != 0)
                genre = "rock";

            rows.Add(new KaggleAudioFeaturesRow(
                $"id{i}", "Song", "Artist", genre, DurationMs: 200_000,
                Danceability: Maybe(random.NextDouble()),
                Energy: Maybe(random.NextDouble()),
                Valence: Maybe(random.NextDouble()),
                Tempo: Maybe(60 + random.NextDouble() * 140),
                Acousticness: Maybe(random.NextDouble()),
                Instrumentalness: Maybe(random.NextDouble() * random.NextDouble()),
                Liveness: Maybe(random.NextDouble()),
                Speechiness: Maybe(random.NextDouble() * 0.5),
                Loudness: Maybe(-30 + random.NextDouble() * 30),
                Key: random.Next(6) == 0 ? null : random.Next(12),
                Mode: random.Next(6) == 0 ? null : random.Next(2),
                TimeSignature: random.Next(6) == 0 ? null : 3 + random.Next(3)));
        }

        return rows;
    }

    private static double? ReferenceMedian(IEnumerable<KaggleAudioFeaturesRow> rows, AudioFeature feature)
    {
        double[] sorted = rows
            .Select(row => AudioFeatureAccessor.ValueOf(row, feature))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Order()
            .ToArray();

        if (sorted.Length == 0)
            return null;

        int middle = sorted.Length / 2;

        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2d;
    }

    private static void AssertEveryMedianIsExact(IReadOnlyList<KaggleAudioFeaturesRow> rows)
    {
        var builder = new AudioFeatureMedianProfileBuilder();
        foreach (KaggleAudioFeaturesRow row in rows)
            builder.Observe(row);

        AudioFeatureMedianProfile profile = builder.Build();

        foreach (AudioFeature feature in AudioFeatureAccessor.All)
        {
            Assert.True(
                ReferenceMedian(rows, feature) == profile.MedianFor(feature, null),
                $"mediana global de {feature}");

            IEnumerable<IGrouping<string, KaggleAudioFeaturesRow>> byGenre = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Genre))
                .GroupBy(row => row.Genre!.Trim().ToLowerInvariant());

            foreach (IGrouping<string, KaggleAudioFeaturesRow> inGenre in byGenre)
            {
                double? expected = ReferenceMedian(inGenre, feature) ?? ReferenceMedian(rows, feature);

                Assert.True(expected == profile.MedianFor(feature, inGenre.Key), $"mediana de {feature} em {inGenre.Key}");
            }
        }
    }

    [Fact]
    public void EveryMedian_OnADeterministicFixture_EqualsTheSortEverythingReference()
        => AssertEveryMedianIsExact(DeterministicRows());

    [Fact]
    public void EveryMedian_WhenNoRowHasAGenre_EqualsTheSortEverythingReference()
        => AssertEveryMedianIsExact(DeterministicRows().Select(row => row with { Genre = null }).ToList());

    [KaggleDatasetFact]
    public async Task EveryMedian_OnTheRealKaggleDataset_EqualsTheSortEverythingReference()
    {
        string csv = FindKaggleCsv()!;

        var rows = new List<KaggleAudioFeaturesRow>();
        await foreach (KaggleAudioFeaturesRow row in new KaggleAudioFeaturesCsvReader().ReadAsync(csv))
            rows.Add(row);

        AssertEveryMedianIsExact(rows);
    }

    internal static string? FindKaggleCsv()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "dataset.csv");
            if (File.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}

/// <summary>
/// <see cref="FactAttribute"/> que se auto-pula quando o <c>dataset.csv</c> (gitignored) não está na raiz do
/// repositório, como no CI. O skip aparece nomeado no resumo.
/// </summary>
public sealed class KaggleDatasetFactAttribute : FactAttribute
{
    public KaggleDatasetFactAttribute()
    {
        if (AudioFeatureMedianProfileBuilderExactnessTests.FindKaggleCsv() is null)
            Skip = "Requer o dataset.csv do Kaggle na raiz do repositório (gitignored).";
    }
}
