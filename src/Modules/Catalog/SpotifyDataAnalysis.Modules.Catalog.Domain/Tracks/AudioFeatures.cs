using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Guards;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Tracks;

/// <summary>
/// Atributos de áudio de uma faixa (danceability, energy, valence, tempo, …) — o "combustível" da EDA (E2)
/// e do modelo de ML (E3). Value object imutável, anexado 1:1 a um <see cref="Track"/>.
///
/// A API nova do Spotify não expõe mais estes atributos (nov/2024), então a origem costuma ser um dataset
/// externo (Kaggle): <see cref="Source"/> registra de onde vieram e <see cref="IsImputed"/> marca valores
/// preenchidos por imputação (tratamento de faltantes — RF3).
/// </summary>
public sealed class AudioFeatures : ValueObject
{
    public double Danceability { get; }
    public double Energy { get; }
    public double Valence { get; }
    public double Tempo { get; }
    public double Acousticness { get; }
    public double Instrumentalness { get; }
    public double Liveness { get; }
    public double Speechiness { get; }
    public double Loudness { get; }
    public int Key { get; }
    public int Mode { get; }
    public int TimeSignature { get; }

    /// <summary>De onde vieram as features (ex.: nome do dataset Kaggle).</summary>
    public string Source { get; }

    /// <summary>True quando algum valor foi preenchido por imputação (não medido).</summary>
    public bool IsImputed { get; }

    private AudioFeatures(
        double danceability, double energy, double valence, double tempo, double acousticness,
        double instrumentalness, double liveness, double speechiness, double loudness,
        int key, int mode, int timeSignature, string source, bool isImputed)
    {
        Danceability = danceability;
        Energy = energy;
        Valence = valence;
        Tempo = tempo;
        Acousticness = acousticness;
        Instrumentalness = instrumentalness;
        Liveness = liveness;
        Speechiness = speechiness;
        Loudness = loudness;
        Key = key;
        Mode = mode;
        TimeSignature = timeSignature;
        Source = source;
        IsImputed = isImputed;
    }

    public static AudioFeatures Create(
        double danceability, double energy, double valence, double tempo, double acousticness,
        double instrumentalness, double liveness, double speechiness, double loudness,
        int key, int mode, int timeSignature, string source, bool isImputed = false)
    {
        Guard.AgainstNullOrWhiteSpace(source, nameof(source));

        return new AudioFeatures(
            danceability, energy, valence, tempo, acousticness, instrumentalness, liveness,
            speechiness, loudness, key, mode, timeSignature, source.Trim(), isImputed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Danceability;
        yield return Energy;
        yield return Valence;
        yield return Tempo;
        yield return Acousticness;
        yield return Instrumentalness;
        yield return Liveness;
        yield return Speechiness;
        yield return Loudness;
        yield return Key;
        yield return Mode;
        yield return TimeSignature;
        yield return Source;
        yield return IsImputed;
    }
}
