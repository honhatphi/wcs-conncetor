namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Immutable snapshot of gateway orchestration status.
/// Returned by GetStatus() for observability.
/// </summary>
public sealed record GatewayStatus
{
    /// <summary>
    /// Total commands currently queued (pending assignment).
    /// </summary>
    public int QueuedCommands { get; init; }

    /// <summary>
    /// Commands currently processing/executing on devices.
    /// </summary>
    public int ProcessingCommands { get; init; }

    /// <summary>
    /// Total commands completed (success or error).
    /// </summary>
    public long CompletedCommands { get; init; }

    /// <summary>
    /// Whether scheduling is paused.
    /// </summary>
    public bool IsPaused { get; init; }

    /// <summary>
    /// Number of active device workers.
    /// </summary>
    public int ActiveWorkers { get; init; }

    /// <summary>
    /// Per-device statistics.
    /// </summary>
    public IReadOnlyList<DeviceStatistics> DeviceStats { get; init; } = [];

    /// <summary>
    /// Snapshot timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

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
