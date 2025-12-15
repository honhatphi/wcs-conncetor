using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Events;

public sealed class TaskFailedEventArgs(string deviceId, int slotId, string taskId, ErrorDetail errorDetail) : EventArgs
{
    public string DeviceId { get; } = deviceId;

    /// <summary>
    /// Slot ID where the task failed (references SlotConfiguration.SlotId).
    /// </summary>
    public int SlotId { get; } = slotId;

    public string TaskId { get; } = taskId;

    public ErrorDetail ErrorDetail { get; } = errorDetail;
}