namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Represents the current status of a device.
/// Contains connection state, readiness, and current activity information.
/// </summary>
public sealed record DeviceStatus
{
    /// <summary>
    /// Device identifier.
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>
    /// Whether the device is physically connected.
    /// </summary>
    public bool IsConnected { get; init; }

    /// <summary>
    /// Whether the link between DLL and PLC program is established.
    /// </summary>
    public bool IsLinkEstablished { get; init; }

    /// <summary>
    /// Whether the device is ready to accept commands.
    /// </summary>
    public bool IsReady { get; init; }

    /// <summary>
    /// The currently executing command ID, or null if device is idle.
    /// </summary>
    public string? CurrentCommandId { get; init; }

    /// <summary>
    /// Current location of the device (if available).
    /// </summary>
    public Location? CurrentLocation { get; init; }

    /// <summary>
    /// Device operational capabilities.
    /// </summary>
    public DeviceCapabilities Capabilities { get; init; } = DeviceCapabilities.Default;

    /// <summary>
    /// Timestamp when the status was retrieved.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
