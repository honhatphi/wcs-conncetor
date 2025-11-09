using TQG.Automation.SDK.Orchestration.Infrastructure;

namespace TQG.Automation.SDK.Orchestration.Workers;

/// <summary>
/// Result aggregator that reads CommandResults from ResultChannel,
/// updates PendingCommandTracker, and enables result observation via ObserveResultsAsync().
/// Also performs cleanup of old completed commands.
/// </summary>
internal sealed class ReplyHub
{
    private readonly OrchestratorChannels _channels;
    private readonly PendingCommandTracker _tracker;
    private readonly TimeSpan _cleanupInterval;
    private readonly TimeSpan _commandRetentionPeriod;

    public ReplyHub(
        OrchestratorChannels channels,
        PendingCommandTracker tracker,
        TimeSpan? cleanupInterval = null,
        TimeSpan? commandRetentionPeriod = null)
    {
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _cleanupInterval = cleanupInterval ?? TimeSpan.FromMinutes(5);
        _commandRetentionPeriod = commandRetentionPeriod ?? TimeSpan.FromHours(1);
    }

    /// <summary>
    /// Runs the ReplyHub loop: reads results, updates tracker, broadcasts to observers, and performs periodic cleanup.
    /// Runs until cancellation is requested.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var lastCleanupTime = DateTimeOffset.UtcNow;

        try
        {
            // Main loop: read from ResultChannel (blocking)
            await foreach (var result in _channels.ResultChannel.Reader.ReadAllAsync(cancellationToken))
            {
                // Update tracker with completion
                _tracker.MarkAsCompleted(result.CommandId, result);

                // Broadcast to all observers via BroadcastChannel
                // Use TryWrite to avoid blocking if observers are slow
                // If broadcast fails, log but don't block DeviceWorkers
                if (!_channels.BroadcastChannel.Writer.TryWrite(result))
                {
                    // BroadcastChannel is unbounded, this should never happen
                    // But handle gracefully just in case
                    try
                    {
                        await _channels.BroadcastChannel.Writer.WriteAsync(result, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Shutting down, ignore
                        break;
                    }
                }

                // Periodic cleanup of old completed commands
                var now = DateTimeOffset.UtcNow;
                if (now - lastCleanupTime > _cleanupInterval)
                {
                    _tracker.CleanupOldCommands(_commandRetentionPeriod);
                    lastCleanupTime = now;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }

        // Final drain: process any remaining results before shutdown
        while (_channels.ResultChannel.Reader.TryRead(out var result))
        {
            _tracker.MarkAsCompleted(result.CommandId, result);

            // Try to broadcast remaining results
            _channels.BroadcastChannel.Writer.TryWrite(result);
        }
    }
}
