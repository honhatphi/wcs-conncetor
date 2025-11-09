using TQG.Automation.SDK.Models;

namespace TQG.Automation.SDK.Orchestration.Models;

/// <summary>
/// Represents a command submitted to the orchestration queue.
/// Immutable envelope containing all command metadata.
/// </summary>
internal sealed record CommandEnvelope
{
    /// <summary>
    /// Unique command identifier from client software (used as primary key).
    /// </summary>
    public required string CommandId { get; init; }

    /// <summary>
    /// Optional: preferred PLC device ID for affinity scheduling.
    /// If null, Matchmaker selects any available device.
    /// </summary>
    public string? PlcDeviceId { get; init; }

    /// <summary>
    /// Type of command (Inbound, Outbound, Transfer).
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

    /// <summary>
    /// Timestamp when command was submitted.
    /// </summary>
    public DateTimeOffset SubmittedAt { get; init; } = DateTimeOffset.UtcNow;
}
