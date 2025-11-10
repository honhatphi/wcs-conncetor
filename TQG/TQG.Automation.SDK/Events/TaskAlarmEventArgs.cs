using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Events;

/// <summary>
/// Event arguments for alarm detected during task execution.
/// Raised when ErrorAlarm flag is detected during command execution.
/// This is an informational event - the task may continue or fail depending on failOnAlarm configuration.
/// </summary>
public sealed class TaskAlarmEventArgs : EventArgs
{
    /// <summary>
    /// Device ID where the alarm occurred.
    /// </summary>
    public string DeviceId { get; }

    /// <summary>
    /// Command/Task ID that encountered the alarm.
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// Error details from PLC (error code and message).
    /// </summary>
    public ErrorDetail Error { get; }

    public TaskAlarmEventArgs(string deviceId, string taskId, ErrorDetail error)
    {
        DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        TaskId = taskId ?? throw new ArgumentNullException(nameof(taskId));
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }
}
