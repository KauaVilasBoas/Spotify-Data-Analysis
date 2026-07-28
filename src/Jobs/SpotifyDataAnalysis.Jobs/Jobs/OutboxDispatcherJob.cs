using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyDataAnalysis.Infrastructure.Outbox;
using SpotifyDataAnalysis.Jobs.Configuration;
using SpotifyDataAnalysis.Jobs.Scheduling;

namespace SpotifyDataAnalysis.Jobs.Jobs;

/// <summary>
/// A scheduled worker that drains the Transactional Outbox.
///
/// On each tick it delegates to <see cref="OutboxDispatcher.ProcessPendingAsync"/>, which publishes
/// pending integration events via <c>IEventBus</c> and marks them processed. All scheduling, per-tick DI
/// scoping and error handling come from <see cref="TimedBackgroundService"/>; this class only expresses
/// the job's intent.
/// </summary>
public sealed class OutboxDispatcherJob : TimedBackgroundService
{
    private readonly OutboxDispatcherOptions _options;

    public OutboxDispatcherJob(
        IServiceScopeFactory scopeFactory,
        IOptions<JobsOptions> options,
        ILogger<OutboxDispatcherJob> logger)
        : base(scopeFactory, logger)
    {
        _options = options.Value.OutboxDispatcher;
    }

    /// <inheritdoc />
    protected override TimeSpan Interval => _options.Interval;

    /// <inheritdoc />
    protected override async Task ExecuteTickAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        // Scoped collaborators resolved from THIS tick's scope (the dispatcher and the module DbContext
        // behind IOutboxDbContext are scoped).
        OutboxDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();

        await dispatcher.ProcessPendingAsync(_options.BatchSize, _options.MaxAttempts, cancellationToken);
    }
}
