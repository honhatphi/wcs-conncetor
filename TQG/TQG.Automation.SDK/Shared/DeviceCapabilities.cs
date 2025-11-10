namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Defines the operational capabilities of a device.
/// Controls which command types the device is allowed to execute.
/// </summary>
public sealed record DeviceCapabilities
{
    /// <summary>
    /// Gets whether the device supports Inbound operations (receiving materials).
    /// Default is true.
    /// </summary>
    public bool SupportsInbound { get; init; } = true;

    /// <summary>
    /// Gets whether the device supports Outbound operations (dispatching materials).
    /// Default is true.
    /// </summary>
    public bool SupportsOutbound { get; init; } = true;

    /// <summary>
    /// Gets whether the device supports Transfer operations (moving materials internally).
    /// Default is true.
    /// </summary>
    public bool SupportsTransfer { get; init; } = true;

    /// <summary>
    /// Gets whether the device supports CheckPallet operations.
    /// Default is true.
    /// </summary>
    public bool SupportsCheckPallet { get; init; } = true;

    /// <summary>
    /// Creates a default DeviceCapabilities with all operations enabled.
    /// </summary>
    public static DeviceCapabilities Default => new()
    {
        SupportsInbound = true,
        SupportsOutbound = true,
        SupportsTransfer = true,
        SupportsCheckPallet = true
    };

    /// <summary>
    /// Creates a DeviceCapabilities with only Outbound enabled.
    /// </summary>
    public static DeviceCapabilities OutboundOnly => new()
    {
        SupportsInbound = false,
        SupportsOutbound = true,
        SupportsTransfer = false,
        SupportsCheckPallet = false
    };

    /// <summary>
    /// Checks if the device supports the specified command type.
    /// </summary>
    /// <param name="commandType">The command type to check.</param>
    /// <returns>True if the device supports this command type, false otherwise.</returns>
    public bool SupportsCommandType(CommandType commandType)
    {
        return commandType switch
        {
            CommandType.Inbound => SupportsInbound,
            CommandType.Outbound => SupportsOutbound,
            CommandType.Transfer => SupportsTransfer,
            CommandType.CheckPallet => SupportsCheckPallet,
            _ => false
        };
    }

    /// <summary>
    /// Validates the capabilities configuration.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when all capabilities are disabled.</exception>
    public void Validate()
    {
        if (!SupportsInbound && !SupportsOutbound && !SupportsTransfer && !SupportsCheckPallet)
        {
            throw new ArgumentException(
                "At least one operation type must be enabled. Device cannot have all capabilities disabled.");
        }
    }
}
