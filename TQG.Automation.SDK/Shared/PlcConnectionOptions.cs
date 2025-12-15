using System.Net;
using TQG.Automation.SDK.Core;

namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Immutable configuration for a PLC connection with multi-slot support.
/// Each device can have multiple slots sharing the same physical connection,
/// enabling parallel command execution (e.g., 2 inbound + 2 outbound simultaneously).
/// </summary>
public sealed record PlcConnectionOptions
{
    /// <summary>
    /// Gets the unique identifier for this PLC device.
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>
    /// Gets the IP address of the PLC.
    /// </summary>
    public required string IpAddress { get; init; }

    /// <summary>
    /// Gets the rack number (typically 0 for S7-1200/1500).
    /// </summary>
    public int Rack { get; init; } = 0;

    /// <summary>
    /// Gets the PLC CPU slot number (typically 1 for CPU).
    /// Note: This is the physical PLC slot, not to be confused with <see cref="SlotConfiguration"/>.
    /// </summary>
    public int Slot { get; init; } = 1;

    /// <summary>
    /// Gets the TCP port (default 102 for S7 protocol).
    /// </summary>
    public int Port { get; init; } = 102;

    /// <summary>
    /// Gets the connection mode (Real or Emulated).
    /// </summary>
    public PlcMode Mode { get; init; } = PlcMode.Real;

    /// <summary>
    /// Gets the connection timeout duration.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the operation (read/write) timeout duration.
    /// </summary>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets the health check interval for automatic reconnection.
    /// </summary>
    public TimeSpan HealthCheckInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the maximum number of reconnection attempts.
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 5;

    /// <summary>
    /// Gets the base delay for exponential backoff during reconnection.
    /// </summary>
    public TimeSpan ReconnectBaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets the signal address template for PLC registers.
    /// Addresses are defined without DB prefix; each slot's <see cref="SlotConfiguration.DbNumber"/>
    /// is used to generate the full address at runtime.
    /// </summary>
    public required SignalMapTemplate SignalMapTemplate { get; init; }

    /// <summary>
    /// Gets the slot configurations for this device.
    /// Each slot represents a logical channel with its own DB number and capabilities.
    /// Multiple slots share the same physical PLC connection.
    /// </summary>
    public required List<SlotConfiguration> Slots { get; init; }

    /// <summary>
    /// Gets the alarm handling behavior during command execution.
    /// Applies to all slots of this device.
    /// When true, command fails immediately when ErrorAlarm is detected.
    /// When false, command continues until CommandFailed or Completed despite alarm.
    /// </summary>
    public bool FailOnAlarm { get; init; } = false;

    /// <summary>
    /// Gets the default command execution timeout.
    /// This value is used when CommandEnvelope.Timeout is not specified.
    /// Default is 15 minutes.
    /// </summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets whether the device should automatically recover from error states.
    /// When false (default), device requires manual recovery via RecoverDeviceAsync().
    /// When true, device will automatically poll and recover when DeviceReady flag becomes true.
    /// </summary>
    public bool AutoRecoveryEnabled { get; init; } = false;

    /// <summary>
    /// Gets the polling interval for checking device recovery status.
    /// Only used when AutoRecoveryEnabled is true or during manual recovery wait.
    /// Default is 5 seconds.
    /// </summary>
    public TimeSpan RecoveryPollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the number of configured slots.
    /// </summary>
    public int SlotCount => Slots?.Count ?? 0;

    /// <summary>
    /// Generates a <see cref="SignalMap"/> for the specified slot.
    /// </summary>
    /// <param name="slotId">The slot identifier (1, 2, 3...).</param>
    /// <returns>A fully-qualified <see cref="SignalMap"/> for the slot.</returns>
    /// <exception cref="ArgumentException">Thrown when slot is not found.</exception>
    public SignalMap GetSlotSignalMap(int slotId)
    {
        var slot = Slots?.FirstOrDefault(s => s.SlotId == slotId)
            ?? throw new ArgumentException($"Slot {slotId} not found in device '{DeviceId}'.", nameof(slotId));

        return SignalMapTemplate.ToSignalMap(slot.DbNumber);
    }

    /// <summary>
    /// Gets the configuration for a specific slot.
    /// </summary>
    /// <param name="slotId">The slot identifier (1, 2, 3...).</param>
    /// <returns>The slot configuration.</returns>
    /// <exception cref="ArgumentException">Thrown when slot is not found.</exception>
    public SlotConfiguration GetSlot(int slotId)
    {
        return Slots?.FirstOrDefault(s => s.SlotId == slotId)
            ?? throw new ArgumentException($"Slot {slotId} not found in device '{DeviceId}'.", nameof(slotId));
    }

    /// <summary>
    /// Validates the configuration and throws if invalid.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DeviceId))
            throw new ArgumentException("DeviceId cannot be null or empty.", nameof(DeviceId));

        if (string.IsNullOrWhiteSpace(IpAddress))
            throw new ArgumentException("IpAddress cannot be null or empty.", nameof(IpAddress));

        if (!IPAddress.TryParse(IpAddress, out _))
            throw new ArgumentException($"Invalid IP address: '{IpAddress}'.", nameof(IpAddress));

        if (Port < 1 || Port > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 1 and 65535.");

        if (Rack < 0)
            throw new ArgumentOutOfRangeException(nameof(Rack), Rack, "Rack cannot be negative.");

        if (Slot < 0)
            throw new ArgumentOutOfRangeException(nameof(Slot), Slot, "Slot cannot be negative.");

        if (ConnectTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout), ConnectTimeout, "ConnectTimeout must be positive.");

        if (OperationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(OperationTimeout), OperationTimeout, "OperationTimeout must be positive.");

        if (HealthCheckInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(HealthCheckInterval), HealthCheckInterval, "HealthCheckInterval must be positive.");

        if (MaxReconnectAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxReconnectAttempts), MaxReconnectAttempts, "MaxReconnectAttempts cannot be negative.");

        if (ReconnectBaseDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ReconnectBaseDelay), ReconnectBaseDelay, "ReconnectBaseDelay must be positive.");

        if (CommandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(CommandTimeout), CommandTimeout, "CommandTimeout must be positive.");

        if (RecoveryPollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RecoveryPollInterval), RecoveryPollInterval, "RecoveryPollInterval must be positive.");

        // Validate signal map template
        if (SignalMapTemplate == null)
            throw new ArgumentNullException(nameof(SignalMapTemplate), "SignalMapTemplate cannot be null.");

        SignalMapTemplate.Validate();

        // Validate slots
        if (Slots == null || Slots.Count == 0)
            throw new ArgumentException("At least one slot must be configured.", nameof(Slots));

        // Validate each slot and check for duplicates
        var slotIds = new HashSet<int>();
        var dbNumbers = new HashSet<int>();

        foreach (var slot in Slots)
        {
            slot.Validate();

            if (!slotIds.Add(slot.SlotId))
                throw new ArgumentException($"Duplicate SlotId found: {slot.SlotId}. SlotIds must be unique within a device.", nameof(Slots));

            if (!dbNumbers.Add(slot.DbNumber))
                throw new ArgumentException($"Duplicate DbNumber found: {slot.DbNumber}. Each slot must have a unique DB number.", nameof(Slots));
        }
    }
}
