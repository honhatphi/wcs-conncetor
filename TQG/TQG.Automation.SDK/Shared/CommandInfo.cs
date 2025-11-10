namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Public command tracking information.
/// Represents the state and execution details of a command.
/// </summary>
public sealed record CommandInfo
{
    /// <summary>
    /// Client command identifier.
    /// </summary>
    public required string CommandId { get; init; }

    /// <summary>
    /// Device ID executing or assigned to this command.
    /// </summary>
    public string? PlcDeviceId { get; init; }

    /// <summary>
    /// Command type (Inbound, Outbound, Transfer).
    /// </summary>
    public required CommandType CommandType { get; init; }

    /// <summary>
    /// Current state of the command (Pending, Processing, Completed, etc.).
    /// </summary>
    public required string State { get; init; }

    /// <summary>
    /// Execution status (Success, Failed, Warning, Error, etc.).
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Source location for the command.
    /// </summary>
    public Location? SourceLocation { get; init; }

    /// <summary>
    /// Destination location for the command.
    /// </summary>
    public Location? DestinationLocation { get; init; }

    /// <summary>
    /// Gate number for Inbound/Outbound operations.
    /// </summary>
    public int GateNumber { get; init; }

    /// <summary>
    /// When the command was submitted.
    /// </summary>
    public DateTimeOffset SubmittedAt { get; init; }

    /// <summary>
    /// When the command started executing (null if not started yet).
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// When the command completed (null if not completed yet).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Result message from execution.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Indicates if pallet is available (for CheckPallet operations).
    /// </summary>
    public bool? PalletAvailable { get; init; }

    /// <summary>
    /// Indicates if pallet is unavailable (for CheckPallet operations).
    /// </summary>
    public bool? PalletUnavailable { get; init; }

    /// <summary>
    /// PLC error information if an error/alarm was encountered during execution.
    /// Contains error code and descriptive message.
    /// </summary>
    public ErrorDetail? PlcError { get; init; }

    /// <summary>
    /// Detailed execution steps.
    /// </summary>
    public IReadOnlyList<string> ExecutionSteps { get; init; } = [];
}
