namespace TQG.Automation.SDK.Events;

public class TaskSucceededEventArgs(string deviceId, string taskId) : EventArgs
{
    public string DeviceId { get; } = deviceId;

    public string TaskId { get; } = taskId;
}