namespace TQG.Automation.SDK.Orchestration.Models;

/// <summary>
/// Internal execution status codes (maps to public CommandStatus).
/// </summary>
internal enum ExecutionStatus
{
    /// <summary>
    /// Command completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// Command completed with warnings.
    /// </summary>
    Warning,

    /// <summary>
    /// Command failed due to error during execution (e.g., connection issue, invalid data).
    /// </summary>
    Error,

    /// <summary>
    /// Command was rejected or failed by PLC (CommandFailed flag set).
    /// </summary>
    Failed,

    /// <summary>
    /// Command was cancelled before completion.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Command execution timed out.
    /// </summary>
    Timeout
}
