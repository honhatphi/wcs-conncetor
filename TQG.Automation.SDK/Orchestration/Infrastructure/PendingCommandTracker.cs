using System.Collections.Concurrent;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Infrastructure;

/// <summary>
/// Thread-safe tracker for command lifecycle states.
/// Maintains in-memory state of pending, in-flight, and completed commands.
/// Also tracks global alarm state to block new command dispatching when any device has an active alarm.
/// Uses client-provided CommandId (string) as primary key.
/// </summary>
internal sealed class PendingCommandTracker
{
    private readonly ConcurrentDictionary<string, CommandState> _commandStates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CommandTrackingInfo> _commandInfo = new(StringComparer.Ordinal);

    /// <summary>
    /// Tracks commands that have active alarms (received Alarm notification but not yet completed).
    /// Used to block all new command dispatching when any device has an alarm.
    /// Key: CommandId, Value: ErrorDetail of the alarm.
    /// </summary>
    private readonly ConcurrentDictionary<string, ErrorDetail> _activeAlarms = new(StringComparer.Ordinal);

    private long _totalSubmitted;
    private long _totalCompleted;
    private long _totalErrors;

    /// <summary>
    /// Marks a command as pending in the queue.
    /// </summary>
    /// <param name="commandId">Client command identifier.</param>
    /// <param name="envelope">Command envelope.</param>
    public void MarkAsPending(string commandId, CommandEnvelope envelope)
    {
        _commandStates[commandId] = CommandState.Pending;
        _commandInfo[commandId] = new CommandTrackingInfo
        {
            CommandId = commandId,
            State = CommandState.Pending,
            SubmittedAt = envelope.SubmittedAt,
            PlcDeviceId = envelope.PlcDeviceId,
            CommandType = envelope.CommandType,
            SourceLocation = envelope.SourceLocation,
            DestinationLocation = envelope.DestinationLocation,
            GateNumber = envelope.GateNumber
        };

        Interlocked.Increment(ref _totalSubmitted);
    }

    /// <summary>
    /// Marks a command as processing (being executed by a device).
    /// </summary>
    /// <param name="commandId">Client command identifier.</param>
    /// <param name="deviceId">Device executing the command.</param>
    public void MarkAsProcessing(string commandId, string deviceId)
    {
        if (_commandStates.TryGetValue(commandId, out var currentState) &&
            currentState == CommandState.Pending)
        {
            _commandStates[commandId] = CommandState.Processing;

            if (_commandInfo.TryGetValue(commandId, out var info))
            {
                info.State = CommandState.Processing;
                info.PlcDeviceId = deviceId;
                info.StartedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>
    /// Marks a command as completed.
    /// </summary>
    /// <param name="commandId">Client command identifier.</param>
    /// <param name="result">Execution result.</param>
    public void MarkAsCompleted(string commandId, CommandResult result)
    {
        _commandStates[commandId] = CommandState.Completed;

        if (_commandInfo.TryGetValue(commandId, out var info))
        {
            info.State = CommandState.Completed;
            info.CompletedAt = result.CompletedAt;
            info.Status = result.Status;
            info.PalletAvailable = result.PalletAvailable;
            info.PalletUnavailable = result.PalletUnavailable;
            info.PlcError = result.PlcError;
        }

        // Clear alarm when command completes (regardless of status)
        ClearAlarm(commandId);

        Interlocked.Increment(ref _totalCompleted);

        if (result.Status == ExecutionStatus.Alarm)
        {
            Interlocked.Increment(ref _totalErrors);
        }
    }

    /// <summary>
    /// Sets an active alarm for a command.
    /// Called when Alarm notification is received during command execution.
    /// Blocks all new command dispatching until alarm is cleared.
    /// </summary>
    /// <param name="commandId">Command that has the alarm.</param>
    /// <param name="error">Error details of the alarm.</param>
    public void SetAlarm(string commandId, ErrorDetail error)
    {
        _activeAlarms[commandId] = error;
    }

    /// <summary>
    /// Clears an active alarm for a command.
    /// Called when command execution completes (success or failure).
    /// </summary>
    /// <param name="commandId">Command to clear alarm for.</param>
    public void ClearAlarm(string commandId)
    {
        _activeAlarms.TryRemove(commandId, out _);
    }

    /// <summary>
    /// Checks if there are any active alarms in the system.
    /// Used by Matchmaker to block all new command dispatching when any device has an alarm.
    /// This prevents sending new commands while a device is in error state and not yet recovered.
    /// </summary>
    /// <returns>True if any command has an active alarm, false otherwise.</returns>
    public bool HasActiveAlarm()
    {
        return !_activeAlarms.IsEmpty;
    }

    /// <summary>
    /// Gets all active alarms in the system.
    /// </summary>
    /// <returns>Dictionary of command IDs to their error details.</returns>
    public IReadOnlyDictionary<string, ErrorDetail> GetActiveAlarms()
    {
        return _activeAlarms.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Marks a command as removed (soft delete).
    /// </summary>
    /// <param name="commandId">Client command identifier.</param>
    /// <returns>True if command was pending and successfully marked as removed.</returns>
    public bool MarkAsRemoved(string commandId)
    {
        if (_commandStates.TryGetValue(commandId, out var currentState) &&
            currentState == CommandState.Pending)
        {
            _commandStates[commandId] = CommandState.Removed;

            if (_commandInfo.TryGetValue(commandId, out var info))
            {
                info.State = CommandState.Removed;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the current state of a command.
    /// </summary>
    /// <param name="commandId">Client command identifier.</param>
    /// <returns>Command state, or NotFound if not tracked.</returns>
    public CommandState GetCommandState(string commandId)
    {
        return _commandStates.TryGetValue(commandId, out var state)
            ? state
            : CommandState.NotFound;
    }

    /// <summary>
    /// Gets detailed tracking information for a command.
    /// </summary>
    /// <param name="commandId">Client command identifier.</param>
    /// <returns>Tracking info, or null if not found.</returns>
    public CommandTrackingInfo? GetCommandInfo(string commandId)
    {
        return _commandInfo.TryGetValue(commandId, out var info) ? info : null;
    }

    /// <summary>
    /// Gets count of commands in pending state.
    /// </summary>
    public int GetPendingCount()
    {
        return _commandStates.Count(kvp => kvp.Value == CommandState.Pending);
    }

    /// <summary>
    /// Gets count of commands in processing state.
    /// </summary>
    public int GetProcessingCount()
    {
        return _commandStates.Count(kvp => kvp.Value == CommandState.Processing);
    }

    /// <summary>
    /// Gets total commands submitted.
    /// </summary>
    public long GetTotalSubmitted() => Interlocked.Read(ref _totalSubmitted);

    /// <summary>
    /// Gets total commands completed.
    /// </summary>
    public long GetTotalCompleted() => Interlocked.Read(ref _totalCompleted);

    /// <summary>
    /// Gets total commands that failed.
    /// </summary>
    public long GetTotalErrors() => Interlocked.Read(ref _totalErrors);

    /// <summary>
    /// Gets per-device statistics.
    /// </summary>
    public IReadOnlyList<DeviceStatistics> GetDeviceStatistics()
    {
        var deviceGroups = _commandInfo.Values
            .Where(info => info.PlcDeviceId != null)
            .GroupBy(info => info.PlcDeviceId!);

        return deviceGroups.Select(group => new DeviceStatistics
        {
            DeviceId = group.Key,
            QueueDepth = group.Count(info => info.State == CommandState.Pending),
            CompletedCount = group.Count(info => info.State == CommandState.Completed),
            ErrorCount = group.Count(info => info.Status == ExecutionStatus.Alarm),
            IsAvailable = !group.Any(info => info.State == CommandState.Processing)
        }).ToList();
    }

    /// <summary>
    /// Gets all tracked commands.
    /// </summary>
    /// <returns>Collection of all command tracking information.</returns>
    public IEnumerable<CommandTrackingInfo> GetAllCommands()
    {
        return _commandInfo.Values.ToList();
    }

    /// <summary>
    /// Gets all commands in pending state (queued, awaiting device assignment).
    /// </summary>
    /// <returns>Collection of pending command tracking information.</returns>
    public IEnumerable<CommandTrackingInfo> GetPendingCommands()
    {
        return _commandInfo.Values
            .Where(info => info.State == CommandState.Pending)
            .OrderBy(info => info.SubmittedAt)
            .ToList();
    }

    /// <summary>
    /// Gets all commands currently processing (executing on devices).
    /// </summary>
    /// <returns>Collection of processing command tracking information.</returns>
    public IEnumerable<CommandTrackingInfo> GetProcessingCommands()
    {
        return _commandInfo.Values
            .Where(info => info.State == CommandState.Processing)
            .OrderBy(info => info.StartedAt)
            .ToList();
    }

    /// <summary>
    /// Cleans up old completed commands from tracking (optional memory management).
    /// </summary>
    /// <param name="olderThan">Remove commands completed before this time.</param>
    public void CleanupOldCommands(TimeSpan olderThan)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;

        var toRemove = _commandInfo.Values
            .Where(info => info.State == CommandState.Completed &&
                           info.CompletedAt.HasValue &&
                           info.CompletedAt.Value < cutoff)
            .Select(info => info.CommandId)
            .ToList();

        foreach (var commandId in toRemove)
        {
            _commandStates.TryRemove(commandId, out _);
            _commandInfo.TryRemove(commandId, out _);
        }
    }
}

/// <summary>
/// Represents the lifecycle state of a command.
/// </summary>
internal enum CommandState
{
    /// <summary>
    /// Command not found in tracking.
    /// </summary>
    NotFound,

    /// <summary>
    /// Command queued, awaiting device assignment.
    /// </summary>
    Pending,

    /// <summary>
    /// Command assigned to device and currently processing/executing.
    /// </summary>
    Processing,

    /// <summary>
    /// Command execution completed (success or error).
    /// </summary>
    Completed,

    /// <summary>
    /// Command removed from queue before execution.
    /// </summary>
    Removed
}

/// <summary>
/// Detailed tracking information for a command.
/// Uses client-provided CommandId as primary key.
/// </summary>
internal sealed class CommandTrackingInfo
{
    public required string CommandId { get; init; }
    public CommandState State { get; set; }
    public DateTimeOffset SubmittedAt { get; init; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? PlcDeviceId { get; set; }
    public ExecutionStatus? Status { get; set; }
    public CommandType CommandType { get; init; }
    public Location? SourceLocation { get; init; }
    public Location? DestinationLocation { get; init; }
    public int GateNumber { get; init; }
    public bool? PalletAvailable { get; set; }
    public bool? PalletUnavailable { get; set; }
    public ErrorDetail? PlcError { get; set; }
}
