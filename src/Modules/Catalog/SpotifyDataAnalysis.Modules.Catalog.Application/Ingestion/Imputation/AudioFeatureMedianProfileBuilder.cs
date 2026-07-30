namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Imputation;

/// <summary>
/// Constrói o <see cref="AudioFeatureMedianProfile"/> a partir das linhas observadas (Builder).
///
/// Acumula apenas os valores <b>presentes</b> — células vazias não entram na distribuição, senão a
/// imputação se retroalimentaria. Ao final, calcula a mediana exata por (gênero, feature) e global.
///
/// <b>Trade-off de memória, assumido:</b> a mediana exata exige guardar as observações (~114k linhas × 12
/// features ≈ dezenas de MB). É aceitável para um dataset local e mantém o resultado exato e reproduzível;
/// se o volume crescer uma ordem de grandeza, a saída é um estimador de quantil por streaming (t-digest),
/// trocando exatidão por memória constante.
/// </summary>
public sealed class AudioFeatureMedianProfileBuilder
{
    private readonly Dictionary<string, Dictionary<AudioFeature, List<double>>> _byGenre = new(StringComparer.Ordinal);
    private readonly Dictionary<AudioFeature, List<double>> _global = new();

    /// <summary>Quantas linhas observadas tinham pelo menos uma feature ausente.</summary>
    public int RowsWithMissingValues { get; private set; }

    /// <summary>Registra uma linha do dataset na distribuição.</summary>
    public void Observe(KaggleAudioFeaturesRow row)
    {
        string? genre = string.IsNullOrWhiteSpace(row.Genre)
            ? null
            : AudioFeatureMedianProfile.NormalizeGenre(row.Genre);

        bool hasMissing = false;

        foreach (AudioFeature feature in AudioFeatureAccessor.All)
        {
            double? value = AudioFeatureAccessor.ValueOf(row, feature);

            if (value is null)
            {
                hasMissing = true;
                continue;
            }

            Bucket(_global, feature).Add(value.Value);

            if (genre is not null)
                Bucket(GenreBuckets(genre), feature).Add(value.Value);
        }

        if (hasMissing)
            RowsWithMissingValues++;
    }

    public AudioFeatureMedianProfile Build()
    {
        Dictionary<string, IReadOnlyDictionary<AudioFeature, double>> byGenre =
            _byGenre.ToDictionary(
                entry => entry.Key,
                entry => ToMedians(entry.Value),
                StringComparer.Ordinal);

        return new AudioFeatureMedianProfile(byGenre, ToMedians(_global));
    }

    private Dictionary<AudioFeature, List<double>> GenreBuckets(string genre)
    {
        if (!_byGenre.TryGetValue(genre, out Dictionary<AudioFeature, List<double>>? buckets))
        {
            buckets = new Dictionary<AudioFeature, List<double>>();
            _byGenre[genre] = buckets;
        }

        return buckets;
    }

    private static List<double> Bucket(Dictionary<AudioFeature, List<double>> buckets, AudioFeature feature)
    {
        if (!buckets.TryGetValue(feature, out List<double>? values))
        {
            values = [];
            buckets[feature] = values;
        }

        return values;
    }

    private static IReadOnlyDictionary<AudioFeature, double> ToMedians(
        Dictionary<AudioFeature, List<double>> buckets)
        => buckets
            .Where(entry => entry.Value.Count > 0)
            .ToDictionary(entry => entry.Key, entry => Median(entry.Value));

    /// <summary>
    /// Mediana clássica: valor central da amostra ordenada, ou a média dos dois centrais quando a contagem
    /// é par.
    /// </summary>
    private static double Median(List<double> values)
    {
        values.Sort();

        int middle = values.Count / 2;

        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2d;
    }
}
