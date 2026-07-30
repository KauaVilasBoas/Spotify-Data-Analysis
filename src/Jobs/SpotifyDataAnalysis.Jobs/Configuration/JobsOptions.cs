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

    /// <summary>Settings for the scheduled playlist collection job.</summary>
    public PlaylistIngestionOptions PlaylistIngestion { get; init; } = new();

    /// <summary>Settings for the scheduled catalog enrichment job (E1.8).</summary>
    public CatalogEnrichmentOptions CatalogEnrichment { get; init; } = new();
}

/// <summary>
/// Settings for the scheduled enrichment of catalog references (E1.8): loading the full artist/album profile
/// from the Spotify Web API for the aggregates that entered the catalog as bare references.
///
/// Disabled by default <b>on purpose</b>, for the same reason as the playlist collection: the job calls the
/// Spotify Web API and would burn rate-limit quota where credentials were not deliberately configured. It is
/// the heaviest API consumer in the project, so its own cadence and batch size are tunable per environment.
/// </summary>
public sealed class CatalogEnrichmentOptions
{
    /// <summary>Whether the scheduled enrichment runs at all. Default: <see langword="false"/>.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// How often a batch of pending references is enriched. Default: 1 hour — the backlog drains a batch per
    /// tick and popularity/followers move slowly, so a tighter cadence spends API quota without adding value.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How many pending artists and albums are enriched per tick. Default: 200 — a batch large enough to make
    /// progress yet small enough to keep each command's transaction short. Zero falls back to the command's
    /// own default.
    /// </summary>
    public int BatchSize { get; init; } = 200;
}

/// <summary>
/// Settings for the scheduled collection of the seed playlists (E1.6).
///
/// Disabled by default <b>on purpose</b>: the job calls the Spotify Web API, so it must never start just
/// because someone ran the host — it only runs where credentials and seeds were deliberately configured.
/// </summary>
public sealed class PlaylistIngestionOptions
{
    /// <summary>Whether the scheduled collection runs at all. Default: <see langword="false"/>.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// How often every seed playlist is re-collected. Default: 6 hours — track popularity moves slowly and
    /// the API has rate limits, so a tighter cadence spends quota without adding information.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Spotify ids of the playlists used as collection seeds. Empty means nothing to collect, which keeps
    /// the job idle instead of failing.
    /// </summary>
    public IReadOnlyList<string> SeedPlaylistIds { get; init; } = [];
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
