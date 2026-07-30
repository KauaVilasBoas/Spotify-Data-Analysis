using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Training;

/// <summary>
/// Fixture determinística com relação sinal-ruído CONHECIDA, usada pelo gate de qualidade (DP-3).
///
/// <para>O gate não pode depender do banco: o catálogo muda a cada ingestão e o teste viraria flaky em CI.
/// Aqui a popularidade é uma função linear conhecida de duas features mais ruído controlado, então um pipeline
/// que aprende PRECISA bater o baseline da média com folga. O que este teste prova é que <b>o pipeline
/// aprende</b>; o que o catálogo real permite aprender é outra pergunta, respondida pela medição registrada
/// no card.</para>
///
/// <para>A geração usa <see cref="Random"/> com semente fixa, cuja sequência o .NET mantém estável
/// justamente para cenários reprodutíveis — a mesma fixture sai em qualquer máquina.</para>
/// </summary>
internal static class LearnableFixture
{
    private const int Seed = 20260730;

    /// <summary>Amplitude do ruído somado ao alvo, em pontos de popularidade.</summary>
    private const double NoiseAmplitude = 6.0;

    /// <summary>
    /// Gera amostras onde <c>popularity ≈ 20 + 60·danceability + 20·energy + ruído</c>. O sinal domina o
    /// ruído de propósito: se o pipeline não aprender ISTO, o problema é o pipeline, não o dado.
    /// </summary>
    public static IReadOnlyList<TrackTrainingSample> Create(int count = 600)
    {
        var random = new Random(Seed);
        var samples = new List<TrackTrainingSample>(count);

        for (int index = 0; index < count; index++)
        {
            double danceability = random.NextDouble();
            double energy = random.NextDouble();
            double noise = (random.NextDouble() - 0.5) * 2 * NoiseAmplitude;

            double popularity = 20 + (60 * danceability) + (20 * energy) + noise;

            samples.Add(SampleOf(
                $"fix-{index:D4}",
                Math.Clamp((int)Math.Round(popularity), 0, 100),
                danceability,
                energy,
                random));
        }

        return samples;
    }

    /// <summary>
    /// Gera amostras SEM sinal nenhum: o alvo é ruído puro, independente das features. Serve para provar que o
    /// gate reprova — um gate que só passa nunca provou nada.
    /// </summary>
    public static IReadOnlyList<TrackTrainingSample> CreateWithoutSignal(int count = 600)
    {
        var random = new Random(Seed);
        var samples = new List<TrackTrainingSample>(count);

        for (int index = 0; index < count; index++)
        {
            samples.Add(SampleOf(
                $"noise-{index:D4}",
                random.Next(0, 101),
                random.NextDouble(),
                random.NextDouble(),
                random));
        }

        return samples;
    }

    private static TrackTrainingSample SampleOf(
        string trackId, int popularity, double danceability, double energy, Random random) =>
        TrackTrainingSample.FromEligibleCandidate(new TrackTrainingCandidate
        {
            TrackId = trackId,
            Popularity = popularity,
            DurationMs = 120_000 + random.Next(0, 180_000),
            Explicit = random.NextDouble() < 0.1,
            HasAudioFeatures = true,
            IsImputed = false,
            Danceability = danceability,
            Energy = energy,
            Valence = random.NextDouble(),
            Tempo = 60 + (random.NextDouble() * 140),
            Acousticness = random.NextDouble(),
            Instrumentalness = random.NextDouble(),
            Liveness = random.NextDouble(),
            Speechiness = random.NextDouble(),
            Loudness = -60 + (random.NextDouble() * 60),
            Key = random.Next(0, 12),
            Mode = random.Next(0, 2),
            TimeSignature = 3 + random.Next(0, 3),
            Genre = "fixture"
        });
}
