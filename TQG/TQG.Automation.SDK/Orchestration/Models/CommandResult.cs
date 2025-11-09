namespace TQG.Automation.SDK.Orchestration.Models;

using TQG.Automation.SDK.Models;

/// <summary>
/// Represents the result of command execution.
/// Published to ResultChannel after worker completes.
/// Uses client-provided CommandId as primary key.
/// </summary>
internal sealed record CommandResult
{
    /// <summary>
    /// Client command identifier for correlation.
    /// </summary>
    public required string CommandId { get; init; }

    /// <summary>
    /// Device that executed the command.
    /// </summary>
    public required string PlcDeviceId { get; init; }

    /// <summary>
    /// Execution status.
    /// </summary>
    public required ExecutionStatus Status { get; init; }

    /// <summary>
    /// Human-readable message (success info, error details, warnings).
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// When execution started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When execution completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Execution duration.
    /// </summary>
    public TimeSpan Duration => CompletedAt - StartedAt;

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
    public PlcError? PlcError { get; init; }

    /// <summary>
    /// Optional: PLC-specific data returned during execution.
    /// </summary>
    public object? Data { get; init; }
}
