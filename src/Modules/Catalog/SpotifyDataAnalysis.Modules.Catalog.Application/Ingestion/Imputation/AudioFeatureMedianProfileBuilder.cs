using System.Runtime.InteropServices;

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
///
/// Cada observação é guardada <b>uma vez só</b>: no bucket do seu gênero, ou no das linhas sem gênero. A
/// mediana global não tem bucket próprio — é a mediana da união de todos eles, montada em <see cref="Build"/>
/// (mesmo multiconjunto, logo o mesmo valor, bit a bit).
/// </summary>
public sealed class AudioFeatureMedianProfileBuilder
{
    private readonly Dictionary<string, Dictionary<AudioFeature, List<double>>> _byGenre = new(StringComparer.Ordinal);
    private readonly Dictionary<AudioFeature, List<double>> _withoutGenre = new();

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

            Bucket(genre is null ? _withoutGenre : GenreBuckets(genre), feature).Add(value.Value);
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

        return new AudioFeatureMedianProfile(byGenre, GlobalMedians());
    }

    private Dictionary<AudioFeature, double> GlobalMedians()
    {
        var medians = new Dictionary<AudioFeature, double>();

        foreach (AudioFeature feature in AudioFeatureAccessor.All)
        {
            List<double>[] sources = _byGenre.Values
                .Select(buckets => buckets.GetValueOrDefault(feature))
                .Append(_withoutGenre.GetValueOrDefault(feature))
                .OfType<List<double>>()
                .ToArray();

            int total = sources.Sum(source => source.Count);

            if (total == 0)
                continue;

            double[] union = new double[total];
            int offset = 0;

            foreach (List<double> source in sources)
            {
                source.CopyTo(union, offset);
                offset += source.Count;
            }

            medians[feature] = Median(union);
        }

        return medians;
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
            .ToDictionary(entry => entry.Key, entry => Median(CollectionsMarshal.AsSpan(entry.Value)));

    /// <summary>
    /// Mediana clássica: valor central da amostra ordenada, ou a média dos dois centrais quando a contagem
    /// é par.
    /// </summary>
    private static double Median(Span<double> values)
    {
        values.Sort();

        int middle = values.Length / 2;

        return values.Length % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2d;
    }
}
