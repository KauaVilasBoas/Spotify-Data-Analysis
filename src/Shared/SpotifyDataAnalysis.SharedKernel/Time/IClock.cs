namespace SpotifyDataAnalysis.SharedKernel.Time;

/// <summary>Clock abstraction — allows substitution in tests without patching static DateTime.Now.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
