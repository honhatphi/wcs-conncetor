namespace TQG.Automation.SDK.Events;

public class TaskSucceededEventArgs(string deviceId, int slotId, string taskId) : EventArgs
{
    public string DeviceId { get; } = deviceId;

    /// <summary>
    /// Slot ID where the task succeeded (references SlotConfiguration.SlotId).
    /// </summary>
    public int SlotId { get; } = slotId;

    public string TaskId { get; } = taskId;
}