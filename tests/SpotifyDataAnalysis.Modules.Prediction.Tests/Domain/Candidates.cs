using SpotifyDataAnalysis.Modules.Prediction.Domain.Training;

namespace SpotifyDataAnalysis.Modules.Prediction.Tests.Domain;

/// <summary>
/// Fábrica de <see cref="TrackTrainingCandidate"/> para os testes: por padrão devolve uma faixa
/// estruturalmente completa e MEDIDA (elegível), que cada teste ajusta via <c>with</c> para exercitar um
/// motivo de exclusão específico.
/// </summary>
internal static class Candidates
{
    public static TrackTrainingCandidate Complete(
        string trackId = "t1", int? popularity = 50, bool isImputed = false)
        => new()
        {
            TrackId = trackId,
            Popularity = popularity,
            DurationMs = 200_000,
            Explicit = false,
            HasAudioFeatures = true,
            IsImputed = isImputed,
            Danceability = 0.5,
            Energy = 0.6,
            Valence = 0.4,
            Tempo = 120.0,
            Acousticness = 0.1,
            Instrumentalness = 0.0,
            Liveness = 0.2,
            Speechiness = 0.05,
            Loudness = -6.0,
            Key = 5,
            Mode = 1,
            TimeSignature = 4,
            Genre = "pop"
        };
}
