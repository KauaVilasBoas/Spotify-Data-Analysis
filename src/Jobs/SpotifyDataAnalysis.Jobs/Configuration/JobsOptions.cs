namespace SpotifyDataAnalysis.Jobs.Configuration;

/// <summary>
/// Strongly-typed configuration for the background jobs, bound from the <c>Jobs</c> section of
/// <c>appsettings</c> (see <c>AddSpotifyJobs</c>). Every value has a safe default so the worker runs even
/// when the section is absent; environments override only what they need.
///
/// Intervals are expressed as <see cref="TimeSpan"/> and parse from the standard <c>"hh:mm:ss"</c>
/// config format (e.g. <c>"00:00:30"</c> = 30s).
/// </summary>
public sealed class JobsOptions
{
    /// <summary>Configuration section name (<c>appsettings.json</c> → <c>"Jobs"</c>).</summary>
    public const string SectionName = "Jobs";

    /// <summary>Settings for the Outbox dispatcher job.</summary>
    public OutboxDispatcherOptions OutboxDispatcher { get; init; } = new();
}

/// <summary>
/// Settings for the Outbox dispatcher job: how often it runs, how many pending messages it drains per
/// tick, and how many times a failing message is retried before being dead-lettered. The defaults
/// (5s cadence, 50 messages, 5 attempts) keep integration events near-real-time without hammering the
/// database and stop a poison message from blocking the loop forever.
/// </summary>
public sealed class OutboxDispatcherOptions
{
    /// <summary>How often the Outbox is scanned for pending messages. Default: 5 seconds.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum pending messages published per tick. Default: 50.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// How many failed delivery attempts a message tolerates before it is dead-lettered and no longer
    /// retried. Default: 5.
    /// </summary>
    public int MaxAttempts { get; init; } = 5;
}
