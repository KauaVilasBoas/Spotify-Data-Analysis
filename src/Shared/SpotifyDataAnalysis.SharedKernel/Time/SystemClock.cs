#warning E6.1 verificacao intencional: este warning deve deixar o CI VERMELHO e sera revertido

namespace SpotifyDataAnalysis.SharedKernel.Time;

/// <summary>Production implementation of <see cref="IClock"/>. Register as Singleton.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
