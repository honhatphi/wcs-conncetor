using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Events;

/// <summary>
/// Event arguments for alarm detected during task execution.
/// Raised when ErrorAlarm flag is detected during command execution.
/// This is an informational event - the task may continue or fail depending on failOnAlarm configuration.
/// </summary>
public sealed class TaskAlarmEventArgs(string deviceId, int slotId, string taskId, ErrorDetail error) : EventArgs
{
    /// <summary>
    /// Device ID where the alarm occurred.
    /// </summary>
    public string DeviceId { get; } = deviceId ?? throw new ArgumentNullException(nameof(deviceId));

    /// <summary>
    /// Slot ID where the alarm occurred (references SlotConfiguration.SlotId).
    /// </summary>
    public int SlotId { get; } = slotId;

    /// <summary>
    /// Command/Task ID that encountered the alarm.
    /// </summary>
    public string TaskId { get; } = taskId ?? throw new ArgumentNullException(nameof(taskId));

    /// <summary>
    /// Error details from PLC (error code and message).
    /// </summary>
    public ErrorDetail Error { get; } = error ?? throw new ArgumentNullException(nameof(error));
}
