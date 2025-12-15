using TQG.Automation.SDK.Shared;

namespace TQG.Automation.SDK.Orchestration.Models;

/// <summary>
/// Result of command execution with detailed steps.
/// </summary>
internal sealed record CommandExecutionResult
{
    public required ExecutionStatus Status { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<string> ExecutionSteps { get; init; } = [];

    /// <summary>
    /// Indicates if pallet is available (for CheckPallet operations).
    /// </summary>
    public bool? PalletAvailable { get; init; }

    /// <summary>
    /// Indicates if pallet is unavailable (for CheckPallet operations).
    /// </summary>
    public bool? PalletUnavailable { get; init; }

    /// <summary>
    /// PLC error information if an error/alarm was encountered.
    /// </summary>
    public ErrorDetail? PlcError { get; init; }

    public static CommandExecutionResult Success(string message, List<string> steps)
        => new() { Status = ExecutionStatus.Success, Message = message, ExecutionSteps = steps };

    public static CommandExecutionResult Warning(string message, List<string> steps)
        => new() { Status = ExecutionStatus.Warning, Message = message, ExecutionSteps = steps };

    public static CommandExecutionResult Error(string message, List<string> steps, ErrorDetail? plcError = null)
        => new() { Status = ExecutionStatus.Error, Message = message, ExecutionSteps = steps, PlcError = plcError };

    public static CommandExecutionResult Failed(string message, List<string> steps, ErrorDetail? plcError = null)
        => new() { Status = ExecutionStatus.Failed, Message = message, ExecutionSteps = steps, PlcError = plcError };

    public static CommandExecutionResult Timeout(string message, List<string> steps)
        => new() { Status = ExecutionStatus.Timeout, Message = message, ExecutionSteps = steps };
}
