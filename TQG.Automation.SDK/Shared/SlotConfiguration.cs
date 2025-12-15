namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Defines configuration for a single slot within a device.
/// Each slot represents a logical channel with its own DB number and capabilities,
/// sharing the same physical PLC connection with other slots.
/// </summary>
public sealed record SlotConfiguration
{
    /// <summary>
    /// Gets the sequential identifier for this slot (1, 2, 3...).
    /// Must be unique within the device.
    /// </summary>
    public required int SlotId { get; init; }

    /// <summary>
    /// Gets the PLC Data Block number for this slot's signals.
    /// Combined with <see cref="SignalMapTemplate"/> to generate full addresses.
    /// Example: DbNumber 33 generates addresses like "DB33.DBX52.0".
    /// </summary>
    public required int DbNumber { get; init; }

    /// <summary>
    /// Gets the operational capabilities of this slot.
    /// Defines which command types (Inbound, Outbound, Transfer, CheckPallet) this slot supports.
    /// Default is all capabilities enabled.
    /// </summary>
    public DeviceCapabilities Capabilities { get; init; } = DeviceCapabilities.Default;

    /// <summary>
    /// Creates a composite identifier combining device ID and slot ID.
    /// Used internally for routing and channel identification.
    /// </summary>
    /// <param name="deviceId">Parent device identifier.</param>
    /// <returns>Composite identifier in format "deviceId:Slot{SlotId}".</returns>
    internal string GetCompositeId(string deviceId) => $"{deviceId}:Slot{SlotId}";

    /// <summary>
    /// Validates the slot configuration.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when SlotId or DbNumber is invalid.</exception>
    /// <exception cref="ArgumentNullException">Thrown when Capabilities is null.</exception>
    /// <exception cref="ArgumentException">Thrown when Capabilities validation fails.</exception>
    public void Validate()
    {
        if (SlotId <= 0)
            throw new ArgumentOutOfRangeException(nameof(SlotId), SlotId, "SlotId must be a positive integer (1, 2, 3...).");

        if (DbNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(DbNumber), DbNumber, "DbNumber must be a positive integer.");

        if (Capabilities == null)
            throw new ArgumentNullException(nameof(Capabilities), "Capabilities cannot be null.");

        Capabilities.Validate();
    }
}
