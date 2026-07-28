using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SpotifyDataAnalysis.Jobs.Scheduling;

/// <summary>
/// Template Method base for every SpotifyDataAnalysis background job.
///
/// It owns the recurring loop so concrete jobs only implement the single tick
/// (<see cref="ExecuteTickAsync"/>) — they never touch the timer, scoping or error handling.
/// Responsibilities encapsulated here:
///
/// <list type="bullet">
///   <item>
///     <b>Drift-free scheduling</b> with <see cref="PeriodicTimer"/>. Unlike a
///     <c>Task.Delay(interval)</c> loop, the period is measured from tick to tick, so a slow tick
///     does not push every subsequent tick later and later. Cancellation stops the wait cleanly.
///   </item>
///   <item>
///     <b>A fresh DI scope per tick</b> (<see cref="IServiceScopeFactory.CreateScope"/>). Jobs need
///     scoped services (DbContext, OutboxDispatcher) but a hosted service is a singleton — resolving
///     scoped services from the root provider would leak/capture them. A per-tick scope also gives each
///     iteration a clean unit of work.
///   </item>
///   <item>
///     <b>Resilience</b>: an exception in one tick is logged and swallowed so the worker survives
///     and retries on the next interval. Only <see cref="OperationCanceledException"/> from shutdown
///     is allowed to stop the loop.
///   </item>
/// </list>
///
/// The <see cref="Interval"/> hook lets each concrete job pull its own cadence from configuration
/// (see <c>JobsOptions</c>), so intervals are per-job and per-environment.
/// </summary>
public abstract class TimedBackgroundService : BackgroundService
{
    protected TimedBackgroundService(IServiceScopeFactory scopeFactory, ILogger logger)
    {
        ScopeFactory = scopeFactory;
        Logger = logger;
    }

    /// <summary>
    /// The DI scope factory shared by the whole job hierarchy. Subclasses that need to open child scopes
    /// use this instead of holding their own duplicate reference.
    /// </summary>
    protected IServiceScopeFactory ScopeFactory { get; }

    /// <summary>
    /// The job's logger, shared with the hierarchy for the same reason as <see cref="ScopeFactory"/>:
    /// subclasses that log their own progress reuse it instead of keeping a duplicate reference to the
    /// very instance they already handed to this base.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>Human-readable job name used in logs (defaults to the concrete type name).</summary>
    protected virtual string JobName => GetType().Name;

    /// <summary>
    /// How often <see cref="ExecuteTickAsync"/> runs. Concrete jobs override this to return their
    /// configured interval (typically from <c>JobsOptions</c>).
    /// </summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>
    /// One unit of work for the job, executed inside a fresh DI <paramref name="scope"/> created for
    /// this tick. Resolve scoped collaborators from <see cref="IServiceScope.ServiceProvider"/>.
    /// Throwing here is safe: the base logs it and the worker continues on the next interval.
    /// </summary>
    protected abstract Task ExecuteTickAsync(IServiceScope scope, CancellationToken cancellationToken);

    /// <inheritdoc />
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation(
            "Job {JobName} started (interval: {Interval}).", JobName, Interval);

        // Run once immediately at startup, then on every timer tick. This drains any Outbox backlog
        // right after a deploy instead of idling for a full interval first.
        await RunTickSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunTickSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown — WaitForNextTickAsync throws when the token is cancelled.
        }

        Logger.LogInformation("Job {JobName} stopped.", JobName);
    }

    private async Task RunTickSafelyAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        try
        {
            // Fresh scope per tick: scoped services (DbContext, OutboxDispatcher) are resolved and disposed
            // within the iteration, never captured across ticks.
            using IServiceScope scope = ScopeFactory.CreateScope();
            await ExecuteTickAsync(scope, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown in the middle of a tick — propagate so the loop can exit.
            throw;
        }
        catch (Exception ex)
        {
            // A failed tick must NOT tear down the worker: log and let the next interval retry.
            Logger.LogError(ex,
                "Job {JobName} tick failed and was skipped; will retry on the next interval.", JobName);
        }
    }
}
