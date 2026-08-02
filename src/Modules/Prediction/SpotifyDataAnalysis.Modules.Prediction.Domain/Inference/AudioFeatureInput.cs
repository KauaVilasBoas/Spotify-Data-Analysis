using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;

/// <summary>
/// O bloco de features que o modelo consome, informado pelo cliente no modo "features à mão" (E3.5). Reflete o
/// feature set que o modelo pode treinar: as nove grandezas contínuas de áudio mais duração e explícito (E3.2)
/// e, a partir do E3.3, as não-contínuas do áudio (<see cref="Key"/>, <see cref="Mode"/>,
/// <see cref="TimeSignature"/>) e o <see cref="Genre"/>.
///
/// <para><b>Anti-skew por construção:</b> o insumo carrega SEMPRE os campos do Bloco A/B; se o feature set do
/// modelo corrente não os consumir, o pipeline apenas não os concatena. Isso evita que um caminho de inferência
/// esqueça de fornecê-los quando o campeão passa a usá-los. Os campos do E3.3 têm <b>defaults neutros</b>
/// (<c>Key</c>=0, <c>Mode</c>=0, <c>TimeSignature</c>=4, <c>Genre</c> ausente) para não quebrar clientes do E3.2
/// que só enviam áudio contínuo — o <c>GET /api/model/current</c> publica quais campos o cliente deve informar
/// para a versão vigente.</para>
///
/// <para>Value object com validação de <b>faixa</b> na criação: as audio-features do Spotify têm domínio
/// conhecido, e aceitar um valor fora dele seria alimentar o modelo com um ponto que ele nunca viu no treino —
/// a predição sairia plausível e sem sentido. A validação mora no domínio, e não só no DTO, porque é regra de
/// negócio da inferência: nenhum caminho (endpoint, teste, job futuro) monta um insumo inválido sem falhar alto.</para>
/// </summary>
public sealed class AudioFeatureInput : ValueObject
{
    /// <summary>Piso e teto (dB) plausíveis para <c>loudness</c> — o Spotify reporta tipicamente -60 a 0.</summary>
    public const double MinimumLoudness = -60.0;
    public const double MaximumLoudness = 5.0;

    /// <summary>Faixa de BPM aceita: um tempo fora disto é erro de digitação, não uma faixa exótica.</summary>
    public const double MinimumTempo = 0.0;
    public const double MaximumTempo = 300.0;

    /// <summary>Duração mínima aceita (1 s) e teto generoso (2 h) — abaixo/acima é dado corrompido, não música.</summary>
    public const int MinimumDurationMs = 1_000;
    public const int MaximumDurationMs = 7_200_000;

    /// <summary>Tonalidade: as 12 classes de altura (0 = Dó … 11 = Si), como o Spotify as codifica.</summary>
    public const int MinimumKey = 0;
    public const int MaximumKey = 11;

    /// <summary>Modo: 0 = menor, 1 = maior. Binário por definição do Spotify.</summary>
    public const int MinimumMode = 0;
    public const int MaximumMode = 1;

    /// <summary>Compasso: o Spotify reporta de 0 a 7 batidas por barra (3 a 7 são os comuns; 0/1 ocorrem).</summary>
    public const int MinimumTimeSignature = 0;
    public const int MaximumTimeSignature = 7;

    /// <summary>Default neutro de <see cref="TimeSignature"/> (4/4) para clientes que não informam o Bloco A.</summary>
    public const int DefaultTimeSignature = 4;

    private AudioFeatureInput(
        double danceability, double energy, double valence, double tempo, double acousticness,
        double instrumentalness, double liveness, double speechiness, double loudness,
        int durationMs, bool @explicit, int key, int mode, int timeSignature, string? genre)
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
        DurationMs = durationMs;
        Explicit = @explicit;
        Key = key;
        Mode = mode;
        TimeSignature = timeSignature;
        Genre = genre;
    }

    public double Danceability { get; }
    public double Energy { get; }
    public double Valence { get; }
    public double Tempo { get; }
    public double Acousticness { get; }
    public double Instrumentalness { get; }
    public double Liveness { get; }
    public double Speechiness { get; }
    public double Loudness { get; }
    public int DurationMs { get; }
    public bool Explicit { get; }

    /// <summary>Tonalidade (0–11). Feature categórica do Bloco A (E3.3), codificada por one-hot no pipeline.</summary>
    public int Key { get; }

    /// <summary>Modo (0 = menor, 1 = maior). Feature binária do Bloco A (E3.3).</summary>
    public int Mode { get; }

    /// <summary>Compasso (0–7). Feature categórica do Bloco A (E3.3), codificada por one-hot no pipeline.</summary>
    public int TimeSignature { get; }

    /// <summary>Gênero da faixa. Feature categórica do Bloco B (E3.3). Ausente vira bucket sentinela no pipeline.</summary>
    public string? Genre { get; }

    /// <summary>
    /// Cria um bloco de features validando a faixa de cada grandeza. Falha alto (<see cref="DomainException"/>)
    /// no primeiro valor fora do domínio, com uma mensagem que nomeia a feature e o intervalo aceito — o
    /// cliente precisa saber exatamente o que corrigir, não receber um "400" opaco. Os campos do Bloco A/B têm
    /// defaults neutros para permitir chamadas que só informam o áudio contínuo do E3.2.
    /// </summary>
    /// <exception cref="DomainException">Quando alguma feature está fora da faixa válida.</exception>
    public static AudioFeatureInput Create(
        double danceability,
        double energy,
        double valence,
        double tempo,
        double acousticness,
        double instrumentalness,
        double liveness,
        double speechiness,
        double loudness,
        int durationMs,
        bool @explicit,
        int key = MinimumKey,
        int mode = MinimumMode,
        int timeSignature = DefaultTimeSignature,
        string? genre = null)
    {
        RequireUnitInterval(danceability, nameof(danceability));
        RequireUnitInterval(energy, nameof(energy));
        RequireUnitInterval(valence, nameof(valence));
        RequireUnitInterval(acousticness, nameof(acousticness));
        RequireUnitInterval(instrumentalness, nameof(instrumentalness));
        RequireUnitInterval(liveness, nameof(liveness));
        RequireUnitInterval(speechiness, nameof(speechiness));

        RequireRange(tempo, MinimumTempo, MaximumTempo, nameof(tempo));
        RequireRange(loudness, MinimumLoudness, MaximumLoudness, nameof(loudness));
        RequireRange(durationMs, MinimumDurationMs, MaximumDurationMs, nameof(durationMs));

        RequireRange(key, MinimumKey, MaximumKey, nameof(key));
        RequireRange(mode, MinimumMode, MaximumMode, nameof(mode));
        RequireRange(timeSignature, MinimumTimeSignature, MaximumTimeSignature, nameof(timeSignature));

        return new AudioFeatureInput(
            danceability, energy, valence, tempo, acousticness, instrumentalness, liveness,
            speechiness, loudness, durationMs, @explicit, key, mode, timeSignature, NormalizeGenre(genre));
    }

    /// <summary>As sete features cujo domínio é o intervalo [0, 1] fechado, por convenção do Spotify.</summary>
    private static void RequireUnitInterval(double value, string featureName) =>
        RequireRange(value, 0.0, 1.0, featureName);

    private static void RequireRange(double value, double minimum, double maximum, string featureName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
            throw new DomainException(
                $"A feature '{featureName}' deve estar no intervalo [{minimum}, {maximum}]. Valor recebido: {value}.");
    }

    /// <summary>Gênero em branco vira ausência explícita (nulo), para o pipeline aplicar o bucket sentinela.</summary>
    private static string? NormalizeGenre(string? genre) =>
        string.IsNullOrWhiteSpace(genre) ? null : genre.Trim();

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
        yield return DurationMs;
        yield return Explicit;
        yield return Key;
        yield return Mode;
        yield return TimeSignature;
        yield return Genre;
    }
}
