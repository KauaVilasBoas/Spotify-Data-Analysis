namespace SpotifyDataAnalysis.Modules.Catalog.Application.Ingestion.Imputation;

/// <summary>
/// Medianas dos atributos de áudio observadas no dataset, <b>estratificadas por gênero</b> e com uma
/// mediana global de retaguarda.
///
/// Por gênero e não global-só: as distribuições são muito diferentes entre estilos (a <i>energy</i> típica
/// de metal não descreve bolero), então imputar pela mediana global empurraria toda faixa incompleta para o
/// centro do dataset inteiro e achataria justamente o sinal que a EDA (E2) e o modelo (E3) procuram.
/// A mediana — e não a média — porque é robusta a outliers, comuns em <i>tempo</i> e <i>loudness</i>.
/// </summary>
public sealed class AudioFeatureMedianProfile
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<AudioFeature, double>> _byGenre;
    private readonly IReadOnlyDictionary<AudioFeature, double> _global;

    internal AudioFeatureMedianProfile(
        IReadOnlyDictionary<string, IReadOnlyDictionary<AudioFeature, double>> byGenre,
        IReadOnlyDictionary<AudioFeature, double> global)
    {
        _byGenre = byGenre;
        _global = global;
    }

    /// <summary>Perfil sem nenhuma observação — nada a imputar (usado quando o dataset vem vazio).</summary>
    public static AudioFeatureMedianProfile Empty { get; } = new(
        new Dictionary<string, IReadOnlyDictionary<AudioFeature, double>>(),
        new Dictionary<AudioFeature, double>());

    /// <summary>
    /// Mediana da feature no gênero informado; cai para a mediana global quando o gênero é desconhecido ou
    /// não tem nenhuma observação daquela feature. <see langword="null"/> quando nem global existe — ou seja,
    /// a feature está ausente no dataset inteiro e não há de onde inferir.
    /// </summary>
    public double? MedianFor(AudioFeature feature, string? genre)
    {
        if (!string.IsNullOrWhiteSpace(genre)
            && _byGenre.TryGetValue(NormalizeGenre(genre), out IReadOnlyDictionary<AudioFeature, double>? medians)
            && medians.TryGetValue(feature, out double genreMedian))
            return genreMedian;

        return _global.TryGetValue(feature, out double globalMedian) ? globalMedian : null;
    }

    internal static string NormalizeGenre(string genre) => genre.Trim().ToLowerInvariant();
}
