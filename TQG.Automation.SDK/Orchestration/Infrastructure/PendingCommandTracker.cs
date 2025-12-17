using System.Collections.Concurrent;
using TQG.Automation.SDK.Orchestration.Models;
using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Infrastructure;

/// <summary>
/// Thread-safe tracker for command lifecycle states.
/// Maintains in-memory state of pending, in-flight, and completed commands.
/// 
/// Device Error Tracking:
/// - Alarms (from PLC ErrorCode): Only needs PLC to clear error flags, no recovery required
/// - Failures (command fail, timeout, code error): Requires manual/auto recovery
/// 
/// Uses client-provided CommandId (string) as primary key.
/// </summary>
internal sealed class PendingCommandTracker
{
    private readonly ConcurrentDictionary<string, CommandState> _commandStates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CommandTrackingInfo> _commandInfo = new(StringComparer.Ordinal);

    /// <summary>
    /// Tracks devices that have active alarms from PLC (ErrorCode != 0).
    /// These do NOT require recovery - just wait for PLC to clear the alarm.
    /// Key: DeviceId, Value: DeviceAlarmInfo with SlotId, ErrorCode, ErrorMessage, ErrorTime.
    /// </summary>
    private readonly ConcurrentDictionary<string, DeviceAlarmInfo> _deviceAlarms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tracks devices that have command failures and REQUIRE recovery (manual or auto).
    /// Key: DeviceId, Value: DeviceFailureInfo with SlotId, ErrorMessage, ErrorTime.
    /// </summary>
    private readonly ConcurrentDictionary<string, DeviceFailureInfo> _deviceFailures = new(StringComparer.OrdinalIgnoreCase);

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

        Interlocked.Increment(ref _totalCompleted);

        if (result.Status == ExecutionStatus.Failed || 
            result.Status == ExecutionStatus.Timeout ||
            result.Status == ExecutionStatus.Alarm)
        {
            Interlocked.Increment(ref _totalErrors);
        }
    }

    #region Device Alarm Tracking (PLC ErrorCode - No Recovery Required)

    /// <summary>
    /// Sets a device into alarm state from PLC ErrorCode.
    /// Alarm does NOT require recovery - just needs PLC to clear the error.
    /// Matchmaker will check PLC status before dispatching.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="slotId">Slot that encountered the alarm.</param>
    /// <param name="errorMessage">Alarm description.</param>
    /// <param name="errorCode">Error code from PLC.</param>
    public void SetDeviceAlarm(string deviceId, int slotId, string errorMessage, int errorCode)
    {
        _deviceAlarms[deviceId] = new DeviceAlarmInfo
        {
            DeviceId = deviceId,
            SlotId = slotId,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            AlarmTime = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Clears the alarm state for a device.
    /// Called when PLC alarm is cleared (ErrorCode = 0) and device is ready.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    public void ClearDeviceAlarm(string deviceId)
    {
        _deviceAlarms.TryRemove(deviceId, out _);
    }

    /// <summary>
    /// Checks if a specific device has an active alarm.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <returns>True if device has alarm, false otherwise.</returns>
    public bool HasDeviceAlarm(string deviceId)
    {
        return _deviceAlarms.ContainsKey(deviceId);
    }

    /// <summary>
    /// Gets alarm info for a specific device.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <returns>Alarm info if exists, null otherwise.</returns>
    public DeviceAlarmInfo? GetDeviceAlarm(string deviceId)
    {
        return _deviceAlarms.TryGetValue(deviceId, out var info) ? info : null;
    }

    #endregion

    #region Device Failure Tracking (Requires Recovery)

    /// <summary>
    /// Sets a device into failure state. REQUIRES recovery (manual or auto).
    /// Called when command fails, times out, or code error occurs.
    /// Device will be blocked until recovery is complete.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="slotId">Slot that encountered the failure.</param>
    /// <param name="errorMessage">Failure description.</param>
    public void SetDeviceFailure(string deviceId, int slotId, string errorMessage)
    {
        _deviceFailures[deviceId] = new DeviceFailureInfo
        {
            DeviceId = deviceId,
            SlotId = slotId,
            ErrorMessage = errorMessage,
            FailureTime = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Clears the failure state for a device.
    /// Called when device recovery is complete (manual or auto).
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    public void ClearDeviceFailure(string deviceId)
    {
        _deviceFailures.TryRemove(deviceId, out _);
    }

    /// <summary>
    /// Checks if a specific device has a failure requiring recovery.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <returns>True if device has failure, false otherwise.</returns>
    public bool HasDeviceFailure(string deviceId)
    {
        return _deviceFailures.ContainsKey(deviceId);
    }

    /// <summary>
    /// Gets failure info for a specific device.
    /// </summary>
    /// <param name="deviceId">Device identifier.</param>
    /// <returns>Failure info if exists, null otherwise.</returns>
    public DeviceFailureInfo? GetDeviceFailure(string deviceId)
    {
        return _deviceFailures.TryGetValue(deviceId, out var info) ? info : null;
    }

    /// <summary>
    /// Gets all devices currently in failure state (requiring recovery).
    /// </summary>
    /// <returns>Dictionary of device IDs to their failure information.</returns>
    public IReadOnlyDictionary<string, DeviceFailureInfo> GetAllDeviceFailures()
    {
        return _deviceFailures.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    #endregion

    #region Legacy Methods (Deprecated - Use Alarm/Failure specific methods)

    /// <summary>
    /// DEPRECATED: Use SetDeviceAlarm or SetDeviceFailure instead.
    /// Sets a device into error state.
    /// </summary>
    [Obsolete("Use SetDeviceAlarm for PLC alarms or SetDeviceFailure for command failures")]
    public void SetDeviceError(string deviceId, int slotId, string errorMessage, int? errorCode = null)
    {
        if (errorCode.HasValue && errorCode.Value != 0)
        {
            // Has error code = PLC alarm
            SetDeviceAlarm(deviceId, slotId, errorMessage, errorCode.Value);
        }
        else
        {
            // No error code = command failure
            SetDeviceFailure(deviceId, slotId, errorMessage);
        }
    }

    /// <summary>
    /// DEPRECATED: Use ClearDeviceAlarm or ClearDeviceFailure instead.
    /// Clears the error state for a device.
    /// </summary>
    [Obsolete("Use ClearDeviceAlarm or ClearDeviceFailure instead")]
    public void ClearDeviceError(string deviceId)
    {
        ClearDeviceAlarm(deviceId);
        ClearDeviceFailure(deviceId);
    }

    /// <summary>
    /// DEPRECATED: Use HasDeviceAlarm or HasDeviceFailure instead.
    /// Checks if a specific device is in error state (alarm OR failure).
    /// </summary>
    [Obsolete("Use HasDeviceAlarm or HasDeviceFailure instead")]
    public bool IsDeviceInError(string deviceId)
    {
        return HasDeviceAlarm(deviceId) || HasDeviceFailure(deviceId);
    }

    /// <summary>
    /// DEPRECATED: Check specific alarm/failure states instead.
    /// Checks if ANY device is in error state.
    /// </summary>
    [Obsolete("Check specific alarm/failure states instead")]
    public bool HasDeviceErrors()
    {
        return !_deviceAlarms.IsEmpty || !_deviceFailures.IsEmpty;
    }

    /// <summary>
    /// DEPRECATED: Use GetAllDeviceFailures instead.
    /// Gets all devices currently in error state.
    /// </summary>
    [Obsolete("Use GetAllDeviceFailures instead")]
    public IReadOnlyDictionary<string, DeviceErrorInfo> GetDeviceErrors()
    {
        // Return failures as DeviceErrorInfo for backward compatibility
        return _deviceFailures.ToDictionary(
            kvp => kvp.Key,
            kvp => new DeviceErrorInfo
            {
                DeviceId = kvp.Value.DeviceId,
                SlotId = kvp.Value.SlotId,
                ErrorMessage = kvp.Value.ErrorMessage,
                ErrorCode = null,
                ErrorTime = kvp.Value.FailureTime
            });
    }

    #endregion

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

/// <summary>
/// Information about a device error state.
/// Unified tracking for both PLC alarms and command failures.
/// DEPRECATED: Use DeviceAlarmInfo or DeviceFailureInfo instead.
/// </summary>
[Obsolete("Use DeviceAlarmInfo or DeviceFailureInfo instead")]
internal sealed class DeviceErrorInfo
{
    /// <summary>
    /// Device identifier.
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>
    /// Slot that encountered the error.
    /// </summary>
    public required int SlotId { get; init; }

    /// <summary>
    /// Error description.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// Error code from PLC (if available).
    /// </summary>
    public int? ErrorCode { get; init; }

    /// <summary>
    /// Time when error occurred.
    /// </summary>
    public required DateTimeOffset ErrorTime { get; init; }
}

/// <summary>
/// Information about a device alarm from PLC.
/// Alarms do NOT require recovery - just wait for PLC to clear.
/// </summary>
internal sealed class DeviceAlarmInfo
{
    /// <summary>
    /// Device identifier.
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>
    /// Slot that encountered the alarm.
    /// </summary>
    public required int SlotId { get; init; }

    /// <summary>
    /// Alarm description.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// Error code from PLC.
    /// </summary>
    public required int ErrorCode { get; init; }

    /// <summary>
    /// Time when alarm occurred.
    /// </summary>
    public required DateTimeOffset AlarmTime { get; init; }
}

/// <summary>
/// Information about a device failure requiring recovery.
/// Failures REQUIRE manual or auto recovery before device can accept new commands.
/// </summary>
internal sealed class DeviceFailureInfo
{
    /// <summary>
    /// Device identifier.
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>
    /// Slot that encountered the failure.
    /// </summary>
    public required int SlotId { get; init; }

    /// <summary>
    /// Failure description.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// Time when failure occurred.
    /// </summary>
    public required DateTimeOffset FailureTime { get; init; }
}
