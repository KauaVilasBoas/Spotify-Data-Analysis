using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;

/// <summary>
/// O bloco de features que o modelo consome, informado pelo cliente no modo "features à mão" (E3.5). É o
/// <b>mesmo feature set</b> que o modelo corrente treinou — as nove grandezas contínuas de áudio mais duração e
/// explícito (E3.2). Não há <c>Genre</c>, <c>Key</c>, <c>Mode</c> nem <c>TimeSignature</c> aqui porque o
/// pipeline campeão ainda não os consome; quando o E3.3 mudar o feature set, este contrato acompanha.
///
/// <para>Value object com validação de <b>faixa</b> na criação: as audio-features do Spotify têm domínio
/// conhecido (0–1, exceto <see cref="Tempo"/>, <see cref="Loudness"/> e a duração), e aceitar um valor fora
/// dele seria alimentar o modelo com um ponto que ele nunca viu no treino — a predição sairia plausível e sem
/// sentido. A validação mora no domínio, e não só no DTO, porque é regra de negócio da inferência: nenhum
/// caminho (endpoint, teste, job futuro) monta um insumo inválido sem falhar alto.</para>
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

    private AudioFeatureInput(
        double danceability, double energy, double valence, double tempo, double acousticness,
        double instrumentalness, double liveness, double speechiness, double loudness,
        int durationMs, bool @explicit)
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

    /// <summary>
    /// Cria um bloco de features validando a faixa de cada grandeza. Falha alto (<see cref="DomainException"/>)
    /// no primeiro valor fora do domínio, com uma mensagem que nomeia a feature e o intervalo aceito — o
    /// cliente precisa saber exatamente o que corrigir, não receber um "400" opaco.
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
        bool @explicit)
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

        return new AudioFeatureInput(
            danceability, energy, valence, tempo, acousticness, instrumentalness, liveness,
            speechiness, loudness, durationMs, @explicit);
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
    }
}
