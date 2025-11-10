using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Events;

public sealed class TaskFailedEventArgs(string deviceId, string taskId, ErrorDetail errorDetail) : EventArgs
{
    public string DeviceId { get; } = deviceId;

    public string TaskId { get; } = taskId;

    public ErrorDetail ErrorDetail { get; } = errorDetail;
}