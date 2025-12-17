using TQG.Automation.SDK.Logging;
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
    private ILogger? _logger;

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
    /// Sets the logger for this ReplyHub.
    /// </summary>
    public void SetLogger(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Runs the ReplyHub loop: reads results, updates tracker, broadcasts to observers, and performs periodic cleanup.
    /// Runs until cancellation is requested.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var lastCleanupTime = DateTimeOffset.UtcNow;
        _logger?.LogInformation("[ReplyHub] Started processing results");

        try
        {
            // Main loop: read from ResultChannel (blocking)
            await foreach (var result in _channels.ResultChannel.Reader.ReadAllAsync(cancellationToken))
            {
                // Handle device error state based on result status
                HandleDeviceErrorState(result);

                // Only mark as completed for FINAL status
                // Alarm status is an intermediate notification - command is still executing
                if (IsFinalStatus(result.Status))
                {
                    _tracker.MarkAsCompleted(result.CommandId, result);
                    
                    // Log based on status
                    if (result.Status == ExecutionStatus.Success)
                    {
                        _logger?.LogInformation($"[ReplyHub] Command {result.CommandId} completed: {result.Status}");
                    }
                    else
                    {
                        var errorInfo = result.PlcError != null ? $" - {result.PlcError.ErrorMessage}" : "";
                        _logger?.LogWarning($"[ReplyHub] Command {result.CommandId} completed: {result.Status}{errorInfo}");
                    }
                }
                else
                {
                    _logger?.LogDebug($"[ReplyHub] Received intermediate status for {result.CommandId}: {result.Status}");
                }

                // Broadcast ALL results to observers (including intermediate Alarm notifications)
                // This allows TaskAlarm event to be raised while keeping command in Processing state
                // Use TryWrite to avoid blocking if observers are slow
                // If broadcast fails, log but don't block DeviceWorkers
                if (!_channels.BroadcastChannel.Writer.TryWrite(result))
                {
                    // BroadcastChannel is unbounded, this should never happen
                    // But handle gracefully just in case
                    _logger?.LogWarning($"[ReplyHub] BroadcastChannel full, waiting to write result for {result.CommandId}");
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
                    _logger?.LogDebug($"[ReplyHub] Cleanup completed, retention: {_commandRetentionPeriod.TotalMinutes} minutes");
                    lastCleanupTime = now;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
            _logger?.LogInformation("[ReplyHub] Shutdown requested");
        }

        // Final drain: process any remaining results before shutdown
        var drainCount = 0;
        while (_channels.ResultChannel.Reader.TryRead(out var result))
        {
            drainCount++;
            // Only mark as completed for FINAL status
            if (IsFinalStatus(result.Status))
            {
                _tracker.MarkAsCompleted(result.CommandId, result);
            }

            // Try to broadcast remaining results
            _channels.BroadcastChannel.Writer.TryWrite(result);
        }
        
        if (drainCount > 0)
        {
            _logger?.LogInformation($"[ReplyHub] Drained {drainCount} remaining result(s) during shutdown");
        }
        
        _logger?.LogInformation("[ReplyHub] Stopped");
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
            ExecutionStatus.Success => true,    // Command completed successfully
            ExecutionStatus.Failed => true,     // Command failed (includes cancelled)
            ExecutionStatus.Timeout => true,    // Command timed out
            ExecutionStatus.Alarm => false,     // Intermediate: Alarm notification, command still executing
            _ => true                           // Unknown status treated as final for safety
        };
    }

    /// <summary>
    /// Handles device alarm/error state based on command result.
    /// 
    /// State Rules:
    /// - Alarm: Set device ALARM (does NOT require recovery, just tracking)
    /// - Success: Clear device ALARM (if any)
    /// - Failed/Timeout: Device FAILURE is set by SlotWorker (requires recovery)
    /// 
    /// Note: Alarms don't block dispatch - they are just tracked for monitoring.
    /// Only failures (set by SlotWorker) block dispatch until recovery.
    /// </summary>
    private void HandleDeviceErrorState(CommandResult result)
    {
        // Skip if no device ID (shouldn't happen, but be safe)
        if (string.IsNullOrEmpty(result.PlcDeviceId))
            return;

        switch (result.Status)
        {
            case ExecutionStatus.Alarm:
                // Alarm detected - track for monitoring but does NOT block dispatch
                // SlotWorker will continue executing and signal availability when done
                var alarmMessage = result.PlcError?.ErrorMessage ?? "Alarm detected";
                var alarmCode = result.PlcError?.ErrorCode ?? 0;
                _tracker.SetDeviceAlarm(result.PlcDeviceId, result.SlotId ?? 0, alarmMessage, alarmCode);
                _logger?.LogWarning($"[ReplyHub] Device {result.PlcDeviceId}/Slot{result.SlotId} alarm recorded: {alarmMessage} (Code: {alarmCode})");
                break;

            case ExecutionStatus.Success:
                // Success - clear device alarm (if any) for this device
                _tracker.ClearDeviceAlarm(result.PlcDeviceId);
                _logger?.LogDebug($"[ReplyHub] Device {result.PlcDeviceId} alarm cleared after successful command");
                break;

            // Failed/Timeout: Device FAILURE is managed by SlotWorker
            // SlotWorker sets failure before publishing, and clears after recovery
            // We don't touch failure state here
        }
    }
}
