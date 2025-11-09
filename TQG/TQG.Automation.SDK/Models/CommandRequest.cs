namespace TQG.Automation.SDK.Models;

/// <summary>
/// Public command request submitted by external consumers.
/// Maps to internal CommandEnvelope.
/// </summary>
public sealed record CommandRequest
{
    /// <summary>
    /// Unique command identifier from client software.
    /// Used for tracking and correlation between client and DLL.
    /// </summary>
    public required string CommandId { get; init; }

    /// <summary>
    /// Optional: preferred PLC device ID for affinity scheduling.
    /// If null, any available device will be selected.
    /// </summary>
    public string? PlcDeviceId { get; init; }

    /// <summary>
    /// Type of command operation.
    /// </summary>
    public required CommandType CommandType { get; init; }

    /// <summary>
    /// Source location in warehouse (Floor, Rail, Block, Depth).
    /// </summary>
    public Location? SourceLocation { get; init; }

    /// <summary>
    /// Destination location in warehouse (Floor, Rail, Block, Depth).
    /// </summary>
    public Location? DestinationLocation { get; init; }

    /// <summary>
    /// Gate number for inbound/outbound operations (if applicable).
    /// </summary>
    public int GateNumber { get; init; }

    /// <summary>
    /// Entry direction for material flow (Top or Bottom).
    /// </summary>
    public Direction? EnterDirection { get; init; }

    /// <summary>
    /// Exit direction for material flow (Top or Bottom).
    /// </summary>
    public Direction? ExitDirection { get; init; }
}
