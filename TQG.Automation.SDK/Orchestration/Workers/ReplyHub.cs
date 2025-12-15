using TQG.Automation.SDK.Orchestration.Infrastructure;
using TQG.Automation.SDK.Orchestration.Models;

namespace TQG.Automation.SDK.Orchestration.Workers;

/// <summary>
/// Result aggregator that reads CommandResults from ResultChannel,
/// updates PendingCommandTracker, and enables result observation via ObserveResultsAsync().
/// Also performs cleanup of old completed commands.
/// 
/// IMPORTANT: Only marks commands as completed for FINAL status (Success, Failed, Timeout, Cancelled, Warning).
/// Intermediate notifications (Error/Alarm) are broadcast but do NOT remove command from Processing state.
/// This prevents race conditions where a new command could be dispatched while the current command
/// is still executing (e.g., after PLC reset but before command execution completes).
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
                // Handle alarm tracking for global alarm state
                if (result.Status == ExecutionStatus.Alarm && result.PlcError != null)
                {
                    // Set global alarm - blocks all new command dispatching
                    _tracker.SetAlarm(result.CommandId, result.PlcError);
                }

                // Only mark as completed for FINAL status
                // Alarm status is an intermediate notification - command is still executing
                if (IsFinalStatus(result.Status))
                {
                    // MarkAsCompleted also clears any active alarm for this command
                    _tracker.MarkAsCompleted(result.CommandId, result);
                }

                // Broadcast ALL results to observers (including intermediate Alarm notifications)
                // This allows TaskAlarm event to be raised while keeping command in Processing state
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
            // Only mark as completed for FINAL status
            if (IsFinalStatus(result.Status))
            {
                _tracker.MarkAsCompleted(result.CommandId, result);
            }

            // Try to broadcast remaining results
            _channels.BroadcastChannel.Writer.TryWrite(result);
        }
    }

    /// <summary>
    /// Determines if an execution status represents a final command result.
    /// 
    /// Final statuses indicate command execution has completed (success or failure).
    /// Intermediate statuses (Alarm) are notifications during execution - command is still running.
    /// 
    /// This distinction is critical for preventing race conditions:
    /// - Final status: Command removed from Processing, device can accept new commands
    /// - Intermediate status: Command stays in Processing, blocks conflicting commands (Inbound/Outbound)
    /// </summary>
    /// <param name="status">The execution status to check.</param>
    /// <returns>True if this is a final status, false if intermediate.</returns>
    private static bool IsFinalStatus(ExecutionStatus status)
    {
        return status switch
        {
            ExecutionStatus.Success => true,  // Command completed successfully
            ExecutionStatus.Failed => true,   // Command failed
            ExecutionStatus.Timeout => true,  // Command timed out
            ExecutionStatus.Alarm => false,   // Intermediate: Alarm notification, command still executing
            _ => true                         // Unknown status treated as final for safety
        };
    }
}
