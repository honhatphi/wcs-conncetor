namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Public notification of command completion.
/// Maps from internal CommandResult for external consumers.
/// Uses client-provided CommandId as primary key.
/// </summary>
public sealed record CommandResultNotification
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
    public required CommandStatus Status { get; init; }

    /// <summary>
    /// Human-readable message (success info, error details, warnings).
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// When execution completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Execution duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// PLC error information if an error/alarm was encountered during execution.
    /// Contains error code and descriptive message.
    /// </summary>
    public ErrorDetail? PlcError { get; init; }
}
