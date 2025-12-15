namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Per-device execution statistics.
/// </summary>
public sealed record DeviceStatistics
{
    /// <summary>
    /// Device identifier.
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>
    /// Number of commands in this device's queue.
    /// </summary>
    public int QueueDepth { get; init; }

    /// <summary>
    /// Total commands completed by this device.
    /// </summary>
    public long CompletedCount { get; init; }

    /// <summary>
    /// Total commands that failed on this device.
    /// </summary>
    public long ErrorCount { get; init; }

    /// <summary>
    /// Whether this device is currently available for work.
    /// </summary>
    public bool IsAvailable { get; init; }
}
