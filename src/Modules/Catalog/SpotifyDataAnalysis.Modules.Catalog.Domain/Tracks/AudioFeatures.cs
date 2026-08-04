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
    // Os setters são PRIVADOS (não ausentes) de propósito: o EF Core, ao serializar este owned type como JSON
    // (ToJson), só mapeia por convenção propriedades com setter — uma propriedade só-leitura sai como {} no
    // jsonb (bug silencioso: as features somem no banco). O value object segue imutável de fora — só o ctor
    // (materialização do EF ou a factory Create) escreve.
    public double Danceability { get; private set; }
    public double Energy { get; private set; }
    public double Valence { get; private set; }
    public double Tempo { get; private set; }
    public double Acousticness { get; private set; }
    public double Instrumentalness { get; private set; }
    public double Liveness { get; private set; }
    public double Speechiness { get; private set; }
    public double Loudness { get; private set; }
    public int Key { get; private set; }
    public int Mode { get; private set; }
    public int TimeSignature { get; private set; }

    /// <summary>
    /// Gênero declarado pelo dataset para a faixa. É o recorte que a EDA (E2) agrupa, a feature categórica
    /// do modelo (E3) e — já no E1.5 — o estrato usado para imputar valores faltantes pela mediana.
    /// </summary>
    public string? Genre { get; private set; }

    /// <summary>De onde vieram as features (ex.: nome do dataset Kaggle).</summary>
    public string Source { get; private set; }

    /// <summary>True quando algum valor foi preenchido por imputação (não medido).</summary>
    public bool IsImputed { get; private set; }

    // Construtor sem parâmetros para a materialização do EF Core (owned/JSON): a hidratação preenche os
    // campos; Source recebe um placeholder só para satisfazer o não-nulo.
    private AudioFeatures() => Source = string.Empty;

    private AudioFeatures(
        double danceability, double energy, double valence, double tempo, double acousticness,
        double instrumentalness, double liveness, double speechiness, double loudness,
        int key, int mode, int timeSignature, string? genre, string source, bool isImputed)
    {
        Genre = genre;
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
        int key, int mode, int timeSignature, string source, string? genre = null, bool isImputed = false)
    {
        Guard.AgainstNullOrWhiteSpace(source, nameof(source));

        return new AudioFeatures(
            danceability, energy, valence, tempo, acousticness, instrumentalness, liveness,
            speechiness, loudness, key, mode, timeSignature,
            string.IsNullOrWhiteSpace(genre) ? null : genre.Trim().ToLowerInvariant(),
            source.Trim(), isImputed);
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
        yield return Genre;
        yield return Source;
        yield return IsImputed;
    }
}
